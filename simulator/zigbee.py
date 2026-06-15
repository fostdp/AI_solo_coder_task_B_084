
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
VOC_SENSOR_POOL = [f"VOC_{i:04d}" for i in range(1, 51)]
FRASS_IMAGE_SENSOR_POOL = [f"FRIMG{i:04d}" for i in range(1, 31)]

OUTBREAK_STATE = {"active": False, "start_ts": 0, "duration": 3600, "affected_ids": []}

PEST_PROFILES = {
    0: {"ellipticity": 0.85, "aspect_ratio": 2.3, "solidity": 0.92, "grayscale": 145},
    1: {"ellipticity": 0.60, "aspect_ratio": 3.8, "solidity": 0.78, "grayscale": 110},
    2: {"ellipticity": 0.40, "aspect_ratio": 1.2, "solidity": 0.95, "grayscale": 68},
    3: {"ellipticity": 0.72, "aspect_ratio": 1.9, "solidity": 0.70, "grayscale": 160},
    4: {"ellipticity": 0.95, "aspect_ratio": 1.05, "solidity": 0.88, "grayscale": 125}
}


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
    voc_toluene: float = 2.5
    voc_xylene: float = 1.8
    voc_ethylbenzene: float = 0.9
    voc_formaldehyde: float = 8.0
    voc_acetaldehyde: float = 3.2
    voc_1octen3ol: float = 0.15
    voc_geosmin: float = 2.0
    voc_2mib: float = 1.5
    voc_total: float = 20.0
    frass_particle_area: float = 120.0
    frass_particle_count: int = 35
    frass_grayscale: float = 130.0
    frass_texture_entropy: float = 5.2
    frass_ellipticity: float = 0.7
    frass_aspect_ratio: float = 2.0
    frass_solidity: float = 0.85
    frass_magnification: float = 40.0
    frass_image_width: int = 640
    frass_image_height: int = 480
    target_pest_species: int = 0
    outbreak_boost_frass_count: float = 1.0

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

        voc_growth = max(0, temp_factor * hum_factor * 0.003 - 0.0012 + random.gauss(0, 0.15))
        fungi_mag = 1.0
        if outbreak_active and self.sensor_type == "voc":
            fungi_mag = random.uniform(1.8, 3.2)
        self.voc_toluene = max(0.05, min(200, self.voc_toluene * (1 + voc_growth) * fungi_mag))
        self.voc_xylene = max(0.05, min(150, self.voc_xylene * (1 + voc_growth) * fungi_mag))
        self.voc_ethylbenzene = max(0.02, min(80, self.voc_ethylbenzene * (1 + voc_growth) * fungi_mag))
        self.voc_formaldehyde = max(0.1, min(500, self.voc_formaldehyde * (1 + voc_growth) * fungi_mag))
        self.voc_acetaldehyde = max(0.05, min(300, self.voc_acetaldehyde * (1 + voc_growth) * fungi_mag))
        self.voc_1octen3ol = max(0.01, min(20, self.voc_1octen3ol * (1 + voc_growth) * fungi_mag))
        self.voc_geosmin = max(0.01, min(50, self.voc_geosmin * (1 + voc_growth) * fungi_mag))
        self.voc_2mib = max(0.01, min(40, self.voc_2mib * (1 + voc_growth) * fungi_mag))
        self.voc_total = (self.voc_toluene + self.voc_xylene + self.voc_ethylbenzene +
                        self.voc_formaldehyde + self.voc_acetaldehyde +
                        self.voc_1octen3ol + self.voc_geosmin + self.voc_2mib)

        if self.sensor_type == "frass_image":
            profile = PEST_PROFILES[self.target_pest_species]
            noise = lambda v: max(0.01, v * (1 + random.gauss(0, 0.08)))
            self.frass_ellipticity = noise(profile["ellipticity"])
            self.frass_aspect_ratio = noise(profile["aspect_ratio"])
            self.frass_solidity = noise(profile["solidity"])
            self.frass_grayscale = noise(profile["grayscale"])
            self.frass_particle_area = max(10, 150 * (1 + random.gauss(0, 0.1)))
            base_count = int(30 + int(random.gauss(0, 8)))
            count_mult = random.uniform(5, 12) if outbreak_active else 1.0
            self.frass_particle_count = int(max(5, base_count * count_mult))
            self.frass_texture_entropy = max(1.0, min(8.0, 5.5 + random.gauss(0, 0.3)))


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


def build_voc_packet(state: SensorState) -> bytes:
    code_bytes = state.code.encode("ascii")[:7].ljust(7, b"\x00")
    ts = int(time.time())
    packet = bytearray(64)
    packet[0:7] = code_bytes
    packet[7] = 0x03
    packet[8:10] = struct.pack("<H", state.addr & 0xFFFF)
    packet[10:14] = struct.pack("<I", ts)
    packet[14:18] = struct.pack("<f", state.voc_toluene)
    packet[18:22] = struct.pack("<f", state.voc_xylene)
    packet[22:26] = struct.pack("<f", state.voc_ethylbenzene)
    packet[26:30] = struct.pack("<f", state.voc_formaldehyde)
    packet[30:34] = struct.pack("<f", state.voc_acetaldehyde)
    packet[34:38] = struct.pack("<f", state.voc_1octen3ol)
    packet[38:42] = struct.pack("<f", state.voc_geosmin)
    packet[42:46] = struct.pack("<f", state.voc_2mib)
    packet[46:50] = struct.pack("<f", state.voc_total)
    airflow = max(0.05, 0.3 + random.gauss(0, 0.08))
    packet[50:54] = struct.pack("<f", state.last_temp)
    packet[54:58] = struct.pack("<f", state.last_hum)
    packet[58:62] = struct.pack("<f", airflow)
    packet[62:64] = struct.pack("<h", random.randint(-85, -48))
    return bytes(packet)


def build_frass_image_packet(state: SensorState) -> bytes:
    code_bytes = state.code.encode("ascii")[:7].ljust(7, b"\x00")
    ts = int(time.time())
    packet = bytearray(65)
    packet[0:7] = code_bytes
    packet[7] = 0x04
    packet[8:10] = struct.pack("<H", state.addr & 0xFFFF)
    packet[10:14] = struct.pack("<I", ts)
    packet[14:16] = struct.pack("<H", state.frass_image_width)
    packet[16:18] = struct.pack("<H", state.frass_image_height)
    packet[18] = 8
    packet[19:23] = struct.pack("<f", state.frass_magnification)
    packet[23:27] = struct.pack("<f", state.frass_particle_area)
    packet[27:31] = struct.pack("<I", state.frass_particle_count)
    packet[31:35] = struct.pack("<f", state.frass_grayscale)
    packet[35:39] = struct.pack("<f", state.frass_texture_entropy)
    packet[39:43] = struct.pack("<f", state.frass_ellipticity)
    packet[43:47] = struct.pack("<f", state.frass_aspect_ratio)
    packet[47:51] = struct.pack("<f", state.frass_solidity)
    correlated_density = state.frass_particle_count / (state.frass_image_width * state.frass_image_height * 1000.0)
    packet[51:55] = struct.pack("<f", correlated_density)
    packet[55:59] = struct.pack("<f", state.last_temp)
    packet[59:63] = struct.pack("<f", state.last_hum)
    packet[63:65] = struct.pack("<h", random.randint(-90, -52))
    return bytes(packet)


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
            s.outbreak_boost_frass_count = evt.frass_multiplier
        until = time.time() + evt.duration_seconds
        while time.time() < until and not stop_evt.is_set():
            if stop_evt.wait(1.0):
                return
        for s in affected:
            s.outbreak_boost_frass = 1.0
            s.outbreak_boost_holes = 0
            s.outbreak_boost_frass_count = 1.0
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

            r = random.random()
            if s.sensor_type == "dust":
                pkt = build_dust_packet(s)
            elif s.sensor_type == "fungi":
                pkt = build_fungi_packet(s)
            elif s.sensor_type == "voc":
                pkt = build_voc_packet(s)
            elif s.sensor_type == "frass_image":
                pkt = build_frass_image_packet(s)
            else:
                if r < 0.35:
                    pkt = build_dust_packet(s)
                elif r < 0.60:
                    pkt = build_fungi_packet(s)
                elif r < 0.85:
                    pkt = build_voc_packet(s)
                else:
                    pkt = build_frass_image_packet(s)
            batch_buffer.append(pkt)
            tx_count += 1

            seq += 1
            if tx_count % 200 == 0:
                now = datetime.now().strftime("%H:%M:%S")
                outbreak_tag = " [OUTBREAK]" if OUTBREAK_STATE["active"] else ""
                extra_info = ""
                if s.sensor_type == "voc":
                    extra_info = f" VOC_total={s.voc_total:6.2f}"
                elif s.sensor_type == "frass_image":
                    extra_info = f" particles={s.frass_particle_count:4d}"
                print(f"[{now}] TX={tx_count:5d} 碰撞包={collide_count:3d}{outbreak_tag}  "
                      f"最新: {s.code} T={s.last_temp:4.1f}°C RH={s.last_hum:4.1f}% "
                      f"frass={s.last_frass:6.4f} CFU={s.last_cfu:7.1f} holes={s.last_holes}{extra_info}")

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


def build_initial_states(dust_count: int, fungi_count: int,
                         voc_count: int = 0, frass_image_count: int = 0) -> List[SensorState]:
    states: List[SensorState] = []
    all_codes = []
    for _, codes in DYNASTY_SENSOR_CODES.items():
        all_codes.extend(codes)
    random.shuffle(all_codes)

    needed = dust_count + fungi_count + voc_count + frass_image_count
    extended = all_codes * ((needed // len(all_codes)) + 1)
    chosen = extended[:needed]

    tid = 1
    idx = 0
    for i in range(dust_count):
        states.append(SensorState(
            code=chosen[idx],
            addr=0x7800 + 1 + idx,
            sensor_type="dust",
            textile_id=tid
        ))
        idx += 1
        tid += 1
    for i in range(fungi_count):
        states.append(SensorState(
            code=chosen[idx],
            addr=0x7800 + 1 + idx,
            sensor_type="fungi",
            textile_id=tid
        ))
        idx += 1
        tid += 1
    for i in range(voc_count):
        voc_code = VOC_SENSOR_POOL[i % len(VOC_SENSOR_POOL)]
        states.append(SensorState(
            code=voc_code,
            addr=0x7900 + 1 + i,
            sensor_type="voc",
            textile_id=tid
        ))
        idx += 1
        tid += 1
    for i in range(frass_image_count):
        frass_code = FRASS_IMAGE_SENSOR_POOL[i % len(FRASS_IMAGE_SENSOR_POOL)]
        states.append(SensorState(
            code=frass_code,
            addr=0x7A00 + 1 + i,
            sensor_type="frass_image",
            textile_id=tid,
            target_pest_species=i % 5
        ))
        idx += 1
        tid += 1
    random.shuffle(states)
    return states


def run_mesh_broadcast(duration: int, interval: float,
                           dust_sensors: int, fungi_sensors: int,
                           voc_sensors: int, frass_image_sensors: int,
                           enable_outbreak: bool, target_host: str,
                           target_port: int, batch_seconds: int):
    states = build_initial_states(dust_sensors, fungi_sensors, voc_sensors, frass_image_sensors)
    dust_states = [s for s in states if s.sensor_type == "dust"]
    fungi_states = [s for s in states if s.sensor_type == "fungi"]
    voc_states = [s for s in states if s.sensor_type == "voc"]
    frass_image_states = [s for s in states if s.sensor_type == "frass_image"]

    print("=" * 60)
    print("    织绣品ZigBee传感器模拟器 (工程化增强版)")
    print("=" * 60)
    print(f"  粉尘传感器数: {len(dust_states)}")
    print(f"  真菌传感器数: {len(fungi_states)}")
    print(f"  VOC传感器数:  {len(voc_states)}")
    print(f"  蛀虫图像数:  {len(frass_image_states)}")
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
    if voc_states:
        t = threading.Thread(
            target=broadcast_loop,
            args=(voc_states, stop_evt, 1.1, target_host, target_port, batch_seconds),
            daemon=True,
            name="zigbee-voc"
        )
        threads.append(t)
    if frass_image_states:
        t = threading.Thread(
            target=broadcast_loop,
            args=(frass_image_states, stop_evt, 1.6, target_host, target_port, batch_seconds),
            daemon=True,
            name="zigbee-frass-image"
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
    ap.add_argument("--voc-sensors", type=int,
                    default=int(os.environ.get("SIM_VOC_SENSORS", "15")),
                    help="VOC传感器数量")
    ap.add_argument("--frass-image-sensors", type=int,
                    default=int(os.environ.get("SIM_FRASS_IMAGE_SENSORS", "10")),
                    help="蛀虫排泄物显微图像传感器数量")
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
        voc_sensors=args.voc_sensors,
        frass_image_sensors=args.frass_image_sensors,
        enable_outbreak=args.outbreak,
        target_host=args.target_host,
        target_port=args.target_port,
        batch_seconds=args.batch_seconds
    )


if __name__ == "__main__":
    main()
