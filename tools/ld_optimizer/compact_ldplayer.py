import glob
import os
import shutil
import subprocess
import time

LDPLAYER_DIR = r"C:\LDPlayer\LDPlayer9"
LDCONSOLE = os.path.join(LDPLAYER_DIR, "ldconsole.exe")
VMS_DIR = os.path.join(LDPLAYER_DIR, "vms")


def get_free_space_gb(drive: str = "C:") -> float:
    """Return free disk space in GB for the specified drive."""
    return shutil.disk_usage(drive).free / (1024**3)


def ensure_instances_stopped():
    """Ensure all instances are closed before compacting disk images."""
    if os.path.isfile(LDCONSOLE):
        print("Ensuring all LDPlayer instances are stopped...")
        subprocess.run(
            [LDCONSOLE, "quitall"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        time.sleep(2)


def get_vmdk_list() -> list[str]:
    """Find and return all data.vmdk files sorted by size (largest first)."""
    pattern = os.path.join(VMS_DIR, "leidian*", "data.vmdk")
    files = glob.glob(pattern)
    files.sort(key=lambda p: os.path.getsize(p), reverse=True)
    return files


def compress_vmdk(vmdk_path: str, index: int, total: int):
    instance_name = os.path.basename(os.path.dirname(vmdk_path))
    size_gb = os.path.getsize(vmdk_path) / (1024**3)
    print(f"[{index}/{total}] {instance_name} ({size_gb:.2f} GB)...", flush=True)

    # Windows native transparent NTFS compression
    cmd = ["compact.exe", "/c", vmdk_path]
    res = subprocess.run(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )

    if res.returncode == 0:
        for line in res.stdout.splitlines():
            line_clean = line.strip()
            if "total bytes of data" in line_clean or "compression ratio" in line_clean:
                print(f"       -> {line_clean}")
    else:
        print(f"       [!] Warning: {res.stderr.strip()}")


def main():
    if not os.path.isdir(VMS_DIR):
        print(f"Error: Could not locate LDPlayer vms directory at: {VMS_DIR}")
        return

    ensure_instances_stopped()

    vmdks = get_vmdk_list()
    if not vmdks:
        print("No instance virtual disks found.")
        return

    start_free = get_free_space_gb("C:")
    total_raw_gb = sum(os.path.getsize(p) for p in vmdks) / (1024**3)

    print(f"\n==========================================")
    print(f" Found {len(vmdks)} LDPlayer virtual disks")
    print(f" Total uncompressed size: {total_raw_gb:.1f} GB")
    print(f" Initial C: drive free space: {start_free:.1f} GB")
    print(f"==========================================\n")

    start_time = time.time()
    for idx, vmdk in enumerate(vmdks, start=1):
        compress_vmdk(vmdk, idx, len(vmdks))

    end_free = get_free_space_gb("C:")
    reclaimed = end_free - start_free
    elapsed_min = (time.time() - start_time) / 60

    print(f"\n==========================================")
    print(f" Completed in {elapsed_min:.1f} minutes")
    print(f" Final C: drive free space: {end_free:.1f} GB")
    print(f" Total Space Reclaimed: {reclaimed:.1f} GB")
    print(f"==========================================")


if __name__ == "__main__":
    main()
