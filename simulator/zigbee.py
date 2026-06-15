
#!/usr/bin/env python3
"""
ZigBee 粉尘/真菌传感器数据模拟器 (工程化增强版)
- 支持 30+20=50 设备
- 支持 4 小时间隔批量上报
- 支持虫蛀爆发事件注入
"""
import socket
import struct
import random
import time
import json
import threading
import argparse
import os
import sys
import signal
from datetime import datetime, timedelta
from dataclasses import dataclass, field
from typing import List, Optional

ZIGBEE_PORT = int(os.environ.get("ZIGBEE_TARGET_PORT", 8684))
ZIGBEE_TARGET_HOST = os.environ.get("ZIGBEE_TARGET_HOST", "127.0.0.1")
ZIGBEE_MCAST_GRP = os.environ.get("ZIGBEE_MCAST_GRP", "239.255.86.84")

DYNASTY_SENSOR_CODES = {
    "云锦-明": ["YJM0001", "YJM0002", "YJM0003", "YJM0004", "YJM0005", "YJM0006"],
    "蜀锦-清": ["SJQ0011", "SJQ0012", "SJQ0013", "SJQ0014", "SJQ0015"],
    "宋锦-宋": ["SJS0021", "SJS0022", "SJS0023", "SJS0024", "SJS0025", "SJS0026"],
    "缂丝-元": ["KSY0031", "KSY0032", "KSY0033", "KSY0034", "KSY0035", "KSY0036", "KSY0037"],
    "织金锦-元": ["ZJY0041", "ZJY0042", "ZJY0043", "ZJY0044", "ZJY0045"],
    "妆花-清": ["ZHQ0051", "ZHQ0052", "ZHQ0053", "ZHQ0054", "ZHQ0055", "ZHQ0056", "ZHQ0057", "ZHQ0058"]
}

FUNGI_TYPES = ["Aspergillus", "Penicillium", "Cladosporium",
               "Chaetomium", "Trichoderma", "Mucor", "Alternaria"]

DUST_SENSOR_POOL = [f"DUST{i:04d}" for i in range(1, 41)]
FUNGI_SENSOR_POOL = [f"FNGI{i:04d}" for i in range(1, 31)]

OUTBREAK_STATE = {"active": False, "start_ts": 0, "duration": 3600, "affected_ids": []}


@dataclass
class OutbreakEvent:
    start_hours_from_now: float
    duration_seconds: int
    affected_sensor_fraction: float
    frass_multiplier: float
    hole_spike: int


DEFAULT_OUTBREAKS = [
    OutbreakEvent(0.5, 3600, 0.4, 5.0, 3),
    OutbreakEvent(2.0, 7200, 0.6, 8.0, 5),
    OutbreakEvent(3.5, 1800, 0.3, 3.0, 2)
]


@dataclass
class SensorState:
    code: str
    addr: int
    sensor_type: str
    last_pm25: float = 35.0
    last_pm10: float = 60.0
    last_frass: float = 0.02
    last_holes: int = 0
    last_spores: float = 50.0
    last_cfu: float = 120.0
    last_temp: float = 22.0
    last_hum: float = 55.0
    last_dominant: str = "Aspergillus"
    textile_id: int = 0
    drift: float = field(default_factory=lambda: random.uniform(-0.02, 0.02))
    outbreak_boost_frass: float = 1.0
    outbreak_boost_holes: int = 0

    def tick(self, outbreak_active: bool = False):
        env_shift = random.gauss(0, 0.15)
        self.last_temp = max(16, min(32, self.last_temp + env_shift * 0.4))
        self.last_hum = max(35, min(78, self.last_hum + env_shift * 1.2 + self.drift * 2))

        temp_factor = 1.0 + (self.last_temp - 22) * 0.05
        hum_factor = 1.0 + (self.last_hum - 55) * 0.018

        pest_growth = max(0, temp_factor * hum_factor * 0.008 + random.gauss(0, 0.003))
        effective_frass_mult = pest_growth * self.outbreak_boost_frass if outbreak_active else pest_growth
        self.last_frass = max(0.005, min(14, self.last_frass + effective_frass_mult))

        hole_weights = [88, 10, 2]
        extra_holes = self.outbreak_boost_holes if outbreak_active else 0
        self.last_holes += random.choices([0, 1, 2], weights=hole_weights)[0] + extra_holes

        self.last_pm25 = max(8, min(180, self.last_pm25 + random.gauss(0, 4) + self.last_frass * 6))
        self.last_pm10 = max(15, min(260, self.last_pm10 + random.gauss(0, 7) + self.last_frass * 10))

        fungi_growth = max(0, (temp_factor * 0.5 + hum_factor * 0.8 - 0.9) * 0.012 + random.gauss(0, 0.006))
        self.last_spores = max(10, min(4000, self.last_spores * (1 + fungi_growth * 0.25)))
        self.last_cfu = max(50, min(950, self.last_cfu * (1 + fungi_growth * 0.15) + random.gauss(0, 5)))

        if random.random() < 0.03:
            self.last_dominant = random.choice(FUNGI_TYPES)


def build_dust_packet(state: SensorState) -> bytes:
    code_bytes = state.code.encode("ascii")[:7].ljust(7, b"\x00")
    ts = int(time.time())
    packet = bytearray(40)
    packet[0:7] = code_bytes
    packet[7] = 0x01
    packet[8:10] = struct.pack("<H", state.addr & 0xFFFF)
    packet[10:14] = struct.pack("<I", ts)
    packet[14:18] = struct.pack("<f", state.last_pm25)
    packet[18:22] = struct.pack("<f", state.last_pm10)
    packet[22:26] = struct.pack("<f", state.last_frass)
    packet[26:30] = struct.pack("<f", state.last_temp)
    packet[30:34] = struct.pack("<f", state.last_hum)
    packet[34:38] = struct.pack("<i", state.last_holes)
    packet[38:40] = struct.pack("<h", random.randint(-82, -45))
    return bytes(packet)


def build_fungi_packet(state: SensorState) -> bytes:
    code_bytes = state.code.encode("ascii")[:7].ljust(7, b"\x00")
    ts = int(time.time())
    packet = bytearray(68)
    packet[0:7] = code_bytes
    packet[7] = 0x02
    packet[8:10] = struct.pack("<H", state.addr & 0xFFFF)
    packet[10:14] = struct.pack("<I", ts)
    packet[14:22] = struct.pack("<d", state.last_spores)
    packet[22:30] = struct.pack("<d", state.last_cfu)
    packet[30:34] = struct.pack("<f", state.last_temp)
    packet[34:38] = struct.pack("<f", state.last_hum)
    fungi_bytes = state.last_dominant.encode("utf-8")[:32].ljust(32, b"\x00")
    packet[38:70] = fungi_bytes
    packet[66:68] = struct.pack("<h", random.randint(-88, -50))
    return bytes(packet[:68])


def schedule_outbreaks(states: List[SensorState], stop_evt: threading.Event,
                       outbreaks: List[OutbreakEvent], start_time: float):
    pending = sorted(outbreaks, key=lambda o: o.start_hours_from_now)
    idx = 0
    while idx < len(pending) and not stop_evt.is_set():
        evt = pending[idx]
        fire_at = start_time + evt.start_hours_from_now * 3600
        while time.time() < fire_at and not stop_evt.is_set():
            if stop_evt.wait(1.0):
                return
        if stop_evt.is_set():
            return
        OUTBREAK_STATE["active"] = True
        OUTBREAK_STATE["start_ts"] = time.time()
        OUTBREAK_STATE["duration"] = evt.duration_seconds
        affected_count = max(1, int(len(states) * evt.affected_sensor_fraction))
        affected = random.sample(states, affected_count)
        OUTBREAK_STATE["affected_ids"] = [s.code for s in affected]
        print(f"\n[{datetime.now().strftime('%H:%M:%S')}] ⚠️ 虫蛀爆发事件启动！"
              f"影响 {affected_count}/{len(states)} 设备 "
              f"(frass x{evt.frass_multiplier}, holes+{evt.hole_spike})")
        for s in affected:
            s.outbreak_boost_frass = evt.frass_multiplier
            s.outbreak_boost_holes = evt.hole_spike
        until = time.time() + evt.duration_seconds
        while time.time() < until and not stop_evt.is_set():
            if stop_evt.wait(1.0):
                return
        for s in affected:
            s.outbreak_boost_frass = 1.0
            s.outbreak_boost_holes = 0
        OUTBREAK_STATE["active"] = False
        OUTBREAK_STATE["affected_ids"] = []
        print(f"[{datetime.now().strftime('%H:%M:%S')}] ✅ 虫蛀爆发事件结束，已恢复正常\n")
        idx += 1


def broadcast_loop(states, stop_evt, interval, target_host, target_port, batch_seconds=0):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (target_host, target_port)

    seq = 0
    tx_count = 0
    collide_count = 0
    batch_buffer = []
    batch_deadline = 0

    while not stop_evt.is_set():
        for s in states:
            s.tick(outbreak_active=OUTBREAK_STATE["active"])

            sleep_jitter = random.uniform(0, max(0.1, interval / 3.0))
            if stop_evt.wait(sleep_jitter):
                break

            if random.random() < 0.04:
                collide_count += 1
                fake_damage = build_dust_packet(s)
                batch_buffer.append(fake_damage)
                tx_count += 1
                continue

            if random.random() < 0.55 or s.sensor_type == "dust":
                pkt = build_dust_packet(s)
            else:
                pkt = build_fungi_packet(s)
            batch_buffer.append(pkt)
            tx_count += 1

            seq += 1
            if tx_count % 200 == 0:
                now = datetime.now().strftime("%H:%M:%S")
                outbreak_tag = " [OUTBREAK]" if OUTBREAK_STATE["active"] else ""
                print(f"[{now}] TX={tx_count:5d} 碰撞包={collide_count:3d}{outbreak_tag}  "
                      f"最新: {s.code} T={s.last_temp:4.1f}°C RH={s.last_hum:4.1f}% "
                      f"frass={s.last_frass:6.4f} CFU={s.last_cfu:7.1f} holes={s.last_holes}")

        if batch_seconds > 0:
            if batch_deadline == 0:
                batch_deadline = time.time() + batch_seconds
            if batch_buffer and time.time() >= batch_deadline:
                for pkt in batch_buffer:
                    try:
                        sock.sendto(pkt, target)
                    except Exception as e:
                        print(f"[发送错误] {e}")
                if len(batch_buffer) > 0:
                    print(f"[{datetime.now().strftime('%H:%M:%S')}] 📦 批量发送 {len(batch_buffer)} 个数据包")
                batch_buffer = []
                batch_deadline = time.time() + batch_seconds
        else:
            for pkt in batch_buffer:
                try:
                    sock.sendto(pkt, target)
                except Exception as e:
                    print(f"[发送错误] {e}")
            batch_buffer = []

        if stop_evt.wait(interval):
            break

    sock.close()


def build_initial_states(dust_count: int, fungi_count: int) -> List[SensorState]:
    states: List[SensorState] = []
    all_codes = []
    for _, codes in DYNASTY_SENSOR_CODES.items():
        all_codes.extend(codes)
    random.shuffle(all_codes)

    needed = dust_count + fungi_count
    extended = all_codes * ((needed // len(all_codes)) + 1)
    chosen = extended[:needed]

    tid = 1
    for i, code in enumerate(chosen):
        stype = "dust" if i < dust_count else "fungi"
        states.append(SensorState(
            code=code,
            addr=0x7800 + 1 + i,
            sensor_type=stype,
            textile_id=tid
        ))
        tid += 1
    random.shuffle(states)
    return states


def run_mesh_broadcast(duration: int, interval: float,
                       dust_sensors: int, fungi_sensors: int,
                       enable_outbreak: bool, target_host: str,
                       target_port: int, batch_seconds: int):
    states = build_initial_states(dust_sensors, fungi_sensors)
    dust_states = [s for s in states if s.sensor_type == "dust"]
    fungi_states = [s for s in states if s.sensor_type == "fungi"]

    print("=" * 60)
    print("    织绣品ZigBee传感器模拟器 (工程化增强版)")
    print("=" * 60)
    print(f"  粉尘传感器数: {len(dust_states)}")
    print(f"  真菌传感器数: {len(fungi_states)}")
    print(f"  传感器总数:   {len(states)}")
    print(f"  目标地址:     {target_host}:{target_port}")
    print(f"  节点间隔:     {interval}s")
    print(f"  批量发送:     {batch_seconds}s" if batch_seconds > 0 else "  批量发送:     关闭")
    print(f"  虫蛀爆发:     {'启用' if enable_outbreak else '禁用'}")
    print(f"  模拟时长:     {duration if duration > 0 else '∞'}秒")
    print("=" * 60)

    stop_evt = threading.Event()

    threads = []
    if dust_states:
        t = threading.Thread(
            target=broadcast_loop,
            args=(dust_states, stop_evt, interval, target_host, target_port, batch_seconds),
            daemon=True,
            name="zigbee-dust"
        )
        threads.append(t)
    if fungi_states:
        t = threading.Thread(
            target=broadcast_loop,
            args=(fungi_states, stop_evt, interval * 1.3, target_host, target_port, batch_seconds),
            daemon=True,
            name="zigbee-fungi"
        )
        threads.append(t)

    if enable_outbreak:
        t = threading.Thread(
            target=schedule_outbreaks,
            args=(states, stop_evt, DEFAULT_OUTBREAKS, time.time()),
            daemon=True,
            name="outbreak-scheduler"
        )
        threads.append(t)

    for t in threads:
        t.start()

    def handle_sigint(signum, frame):
        print("\n[用户中断] 正在停止所有广播线程...")
        stop_evt.set()

    signal.signal(signal.SIGINT, handle_sigint)
    signal.signal(signal.SIGTERM, handle_sigint)

    start = time.time()
    try:
        while True:
            if duration > 0 and time.time() - start >= duration:
                break
            stop_evt.wait(1.0)
    except KeyboardInterrupt:
        pass

    stop_evt.set()
    for t in threads:
        t.join(timeout=5)
    print(f"[完成] ZigBee广播模拟器已退出")


def parse_env_bool(name: str, default: bool = False) -> bool:
    v = os.environ.get(name)
    if v is None:
        return default
    return v.strip().lower() in ("1", "true", "yes", "y", "on")


def main():
    ap = argparse.ArgumentParser(description="织绣品监测ZigBee传感器数据模拟器")
    ap.add_argument("-d", "--duration", type=int,
                    default=int(os.environ.get("SIM_DURATION", "0")),
                    help="运行时长(秒)，0=无限")
    ap.add_argument("-i", "--interval", type=float,
                    default=float(os.environ.get("SIM_INTERVAL_SECONDS", "0.8")),
                    help="每个节点的发送间隔基础值(秒)")
    ap.add_argument("--dust-sensors", type=int,
                    default=int(os.environ.get("SIM_DUST_SENSORS", "30")),
                    help="粉尘传感器数量")
    ap.add_argument("--fungi-sensors", type=int,
                    default=int(os.environ.get("SIM_FUNGI_SENSORS", "20")),
                    help="真菌传感器数量")
    ap.add_argument("--outbreak", action="store_true",
                    default=parse_env_bool("SIM_OUTBREAK_ENABLED", False),
                    help="启用虫蛀爆发事件注入")
    ap.add_argument("--target-host", type=str,
                    default=os.environ.get("ZIGBEE_TARGET_HOST", "127.0.0.1"),
                    help="目标主机")
    ap.add_argument("--target-port", type=int,
                    default=int(os.environ.get("ZIGBEE_TARGET_PORT", "8684")),
                    help="目标端口")
    ap.add_argument("--batch-seconds", type=int,
                    default=int(os.environ.get("SIM_BATCH_SECONDS", "0")),
                    help="批量发送时间窗口(秒)，0=立即发送")
    args = ap.parse_args()
    run_mesh_broadcast(
        duration=args.duration,
        interval=args.interval,
        dust_sensors=args.dust_sensors,
        fungi_sensors=args.fungi_sensors,
        enable_outbreak=args.outbreak,
        target_host=args.target_host,
        target_port=args.target_port,
        batch_seconds=args.batch_seconds
    )


if __name__ == "__main__":
    main()
