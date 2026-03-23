import json
import subprocess
from pathlib import Path

# ✅ 在这里调偏移（正负秒） Adjust the offset (positive or negative seconds) here
LOAD_OFFSET = 0.45

def find_files():
    cwd = Path(".")
    json_files = list(cwd.glob("*.json"))
    mkv_files = list(cwd.glob("*.mkv"))

    for jf in json_files:
        for vf in mkv_files:
            if jf.stem == vf.stem:
                return jf, vf

    raise FileNotFoundError("未找到同名 json 和 mkv")

def run(cmd):
    print(" ".join(cmd))
    subprocess.run(cmd, check=True)

def main():
    json_file, video_file = find_files()

    with open(json_file, "r", encoding="utf-8") as f:
        data = json.load(f)

    segments = data["valid_segments"]

    # 找录制起点
    start_ts = next(
        e["timestamp"] for e in data["events"]
        if e["type"] == "RecordingStart"
    )

    temp_files = []

    for i, seg in enumerate(segments):
        start = (seg["start"] - start_ts) / 1000.0
        end = (seg["end"] - start_ts) / 1000.0

        # ✅ 应用 Load 偏移（只改 start）
        start += LOAD_OFFSET

        # ✅ 边界保护
        if start >= end:
            continue

        out = f"seg_{i}.mkv"
        temp_files.append(out)

        cmd = [
            "ffmpeg",
            "-y",
            "-ss", str(start),
            "-to", str(end),
            "-i", str(video_file),

            "-c:v", "libx264",
            "-preset", "fast",
            "-crf", "18",

            "-c:a", "aac",
            "-b:a", "192k",

            "-avoid_negative_ts", "make_zero",
            "-fflags", "+genpts",

            out
        ]

        run(cmd)

    # concat 列表
    with open("list.txt", "w", encoding="utf-8") as f:
        for t in temp_files:
            f.write(f"file '{t}'\n")

    final = f"{video_file.stem}_cut.mkv"

    cmd = [
        "ffmpeg",
        "-y",
        "-f", "concat",
        "-safe", "0",
        "-i", "list.txt",
        "-c", "copy",
        "-metadata", "comment=Sts2CutMod=1",
        final
    ]

    run(cmd)

    print("完成:", final)

if __name__ == "__main__":
    main()