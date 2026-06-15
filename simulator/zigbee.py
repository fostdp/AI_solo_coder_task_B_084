
#!/usr/bin/env python3
"""
ZigBee 粉尘/真菌传感器数据模拟器
模拟多个织绣品展区的CC2530终端节点数据广播
"""
import socket
import struct
import random
import time
import json
import threading
import argparse
from datetime import datetime
from dataclasses import dataclass, field

ZIGBEE_PORT = 8684
ZIGBEE_MCAST_GRP = "239.255.86.84"

DYNASTY_SENSOR_CODES = {
    "云锦-明": ["YJM0001", "YJM0002", "YJM0003", "YJM0004"],
    "蜀锦-清": ["SJQ0011", "SJQ0012", "SJQ0013"],
    "宋锦-宋": ["SJS0021", "SJS0022"],
    "缂丝-元": ["KSY0031", "KSY0032", "KSY0033"],
    "织金锦-元": ["ZJY0041", "ZJY0042"],
    "妆花-清": ["ZHQ0051", "ZHQ0052", "ZHQ0053", "ZHQ0054"]
}

FUNGI_TYPES = ["Aspergillus", "Penicillium", "Cladosporium",
               "Chaetomium", "Trichoderma", "Mucor", "Alternaria"]


@dataclass
class SensorState:
    code: str
    addr: int
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

    def tick(self):
        env_shift = random.gauss(0, 0.15)
        self.last_temp = max(16, min(32, self.last_temp + env_shift * 0.4))
        self.last_hum = max(35, min(78, self.last_hum + env_shift * 1.2 + self.drift * 2))

        temp_factor = 1.0 + (self.last_temp - 22) * 0.05
        hum_factor = 1.0 + (self.last_hum - 55) * 0.018

        pest_growth = max(0, temp_factor * hum_factor * 0.008 + random.gauss(0, 0.003))
        self.last_frass = max(0.005, min(14, self.last_frass + pest_growth))

        self.last_holes += random.choices([0, 1, 2], weights=[88, 10, 2])[0]

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


def broadcast_loop(states, stop_evt, interval, use_multicast=True):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    if use_multicast:
        sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 32)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        target = (ZIGBEE_MCAST_GRP, ZIGBEE_PORT)
    else:
        target = ("127.0.0.1", ZIGBEE_PORT)

    seq = 0
    tx_count = 0
    collide_count = 0

    while not stop_evt.is_set():
        for s in states:
            s.tick()

            sleep_jitter = random.uniform(0, interval / 3.0)
            if stop_evt.wait(sleep_jitter):
                break

            if random.random() < 0.04:
                collide_count += 1
                fake_damage = build_dust_packet(s)
                sock.sendto(fake_damage, target)
                tx_count += 1
                continue

            if random.random() < 0.55:
                pkt = build_dust_packet(s)
            else:
                pkt = build_fungi_packet(s)
            sock.sendto(pkt, target)
            tx_count += 1

            seq += 1
            if tx_count % 200 == 0:
                now = datetime.now().strftime("%H:%M:%S")
                print(f"[{now}] TX={tx_count:5d} 碰撞包={collide_count:3d}  "
                      f"最新: {s.code} T={s.last_temp:4.1f}°C RH={s.last_hum:4.1f}% "
                      f"frass={s.last_frass:6.4f} CFU={s.last_cfu:7.1f} holes={s.last_holes}")

        if stop_evt.wait(interval):
            break

    sock.close()


def run_mesh_broadcast(duration: int, interval: float = 0.8):
    states = []
    next_tid = 1
    for _, codes in DYNASTY_SENSOR_CODES.items():
        for i, code in enumerate(codes):
            states.append(SensorState(
                code=code,
                addr=0x7800 + next_tid + i,
                textile_id=next_tid
            ))
        next_tid += len(codes)

    print(f"=== 织绣品ZigBee传感器模拟器启动 ===")
    print(f"节点总数: {len(states)}")
    print(f"目标地址: {ZIGBEE_MCAST_GRP}:{ZIGBEE_PORT} (组播) / 127.0.0.1:{ZIGBEE_PORT} (单播回退)")
    print(f"发送间隔: {interval}s ± {interval/3:.2f}s (有抖动)")
    print(f"模拟时长: {duration if duration > 0 else '∞'}秒")
    print(f"=" * 44)

    stop_evt = threading.Event()

    thread_mesh = threading.Thread(
        target=broadcast_loop,
        args=(states, stop_evt, interval, True),
        daemon=True,
        name="zigbee-mesh-mcast"
    )
    thread_direct = threading.Thread(
        target=broadcast_loop,
        args=(states, stop_evt, interval * 1.3, False),
        daemon=True,
        name="zigbee-direct-ucast"
    )

    thread_mesh.start()
    thread_direct.start()

    start = time.time()
    try:
        while True:
            if duration > 0 and time.time() - start >= duration:
                break
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n[用户中断] 正在停止所有广播线程...")

    stop_evt.set()
    thread_mesh.join(timeout=3)
    thread_direct.join(timeout=3)
    print(f"[完成] ZigBee广播模拟器已退出")


def main():
    ap = argparse.ArgumentParser(description="织绣品监测ZigBee传感器数据模拟器")
    ap.add_argument("-d", "--duration", type=int, default=0, help="运行时长(秒)，0=无限")
    ap.add_argument("-i", "--interval", type=float, default=0.8, help="每个节点的发送间隔基础值")
    args = ap.parse_args()
    run_mesh_broadcast(args.duration, args.interval)


if __name__ == "__main__":
    main()
