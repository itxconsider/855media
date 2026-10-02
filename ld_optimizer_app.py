"""
LDPlayer Multi-Instance Manager & Storage Optimizer
Built for managing, compacting, and mapping LDPlayer 9 instances and cleaning system caches.
"""

import ctypes
from ctypes import wintypes
import glob
import json
import os
import shutil
import stat
import subprocess
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

# Default Paths
DEFAULT_LDPLAYER_DIR = r"C:\LDPlayer\LDPlayer9"
DEFAULT_DESKTOP_DIR = os.path.join(os.path.expanduser("~"), "Desktop")
TEMP_DIR = os.environ.get("TEMP", os.path.join(os.path.expanduser("~"), r"AppData\Local\Temp"))

kernel32 = ctypes.windll.kernel32
GetCompressedFileSizeW = kernel32.GetCompressedFileSizeW
GetCompressedFileSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
GetCompressedFileSizeW.restype = wintypes.DWORD


def get_file_sizes(path: str) -> tuple[int, int]:
    """Return (logical_size, physical_size_on_disk)."""
    if not os.path.isfile(path):
        return 0, 0
    logical = os.path.getsize(path)
    high = wintypes.DWORD()
    low = GetCompressedFileSizeW(path, ctypes.byref(high))
    physical = (high.value << 32) + low
    return logical, physical


def get_free_disk_gb(drive: str = "C:") -> float:
    try:
        return shutil.disk_usage(drive).free / (1024**3)
    except Exception:
        return 0.0


class LDOptimizerApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("LDPlayer Multi-Instance & Storage Optimizer")
        self.geometry("900;700".replace(";", "x"))
        self.minsize(820, 620)
        self.configure(bg="#181825")

        self.ld_dir = tk.StringVar(value=DEFAULT_LDPLAYER_DIR)
        self.desktop_dir = tk.StringVar(value=DEFAULT_DESKTOP_DIR)
        self.status_var = tk.StringVar(value="Ready")
        self.is_busy = False

        self._apply_dark_theme()
        self._build_ui()
        self.refresh_stats()

    def _apply_dark_theme(self):
        self.style = ttk.Style(self)
        self.style.theme_use("clam")

        # Palette
        bg = "#181825"
        card_bg = "#1e1e2e"
        accent = "#89b4fa"
        text_fg = "#cdd6f4"
        sub_fg = "#a6adc8"

        self.style.configure(".", background=bg, foreground=text_fg, font=("Segoe UI", 9))
        self.style.configure("TLabel", background=bg, foreground=text_fg)
        self.style.configure("Card.TFrame", background=card_bg, relief="flat")
        self.style.configure("CardTitle.TLabel", background=card_bg, foreground=accent, font=("Segoe UI", 11, "bold"))
        self.style.configure("CardSub.TLabel", background=card_bg, foreground=sub_fg, font=("Segoe UI", 9))
        self.style.configure("StatNum.TLabel", background=card_bg, foreground="#a6e3a1", font=("Segoe UI", 16, "bold"))

        self.style.configure(
            "Primary.TButton",
            background="#89b4fa",
            foreground="#11111b",
            font=("Segoe UI", 9, "bold"),
            padding=(12, 6),
            borderwidth=0,
        )
        self.style.map("Primary.TButton", background=[("active", "#b4befe")])

        self.style.configure(
            "Action.TButton",
            background="#313244",
            foreground=text_fg,
            font=("Segoe UI", 9),
            padding=(10, 5),
            borderwidth=0,
        )
        self.style.map("Action.TButton", background=[("active", "#45475a")])

        self.style.configure("Horizontal.TProgressbar", background="#89b4fa", troughcolor="#313244", borderwidth=0)

    def _build_ui(self):
        # Header
        header_frame = tk.Frame(self, bg="#11111b", height=60)
        header_frame.pack(fill="x", side="top")

        title_lbl = tk.Label(
            header_frame,
            text="⚡ LDPlayer & System Storage Optimizer",
            font=("Segoe UI", 14, "bold"),
            bg="#11111b",
            fg="#cdd6f4",
        )
        title_lbl.pack(side="left", padx=20, pady=12)

        refresh_btn = ttk.Button(header_frame, text="🔄 Refresh Stats", style="Action.TButton", command=self.refresh_stats)
        refresh_btn.pack(side="right", padx=20, pady=12)

        main_container = tk.Frame(self, bg="#181825")
        main_container.pack(fill="both", expand=True, padx=20, pady=15)

        # Top Stats Cards
        stats_frame = tk.Frame(main_container, bg="#181825")
        stats_frame.pack(fill="x", pady=(0, 15))

        self.stat_c_free = self._create_stat_card(stats_frame, "C: Free Space", "Loading...", "#a6e3a1", 0)
        self.stat_inst_count = self._create_stat_card(stats_frame, "Detected Instances", "Loading...", "#89b4fa", 1)
        self.stat_vmdk_saved = self._create_stat_card(stats_frame, "NTFS Space Saved", "Loading...", "#fab387", 2)

        # Center Notebook / Action Tabs
        action_card = ttk.Frame(main_container, style="Card.TFrame")
        action_card.pack(fill="x", pady=(0, 15), ipady=10)

        # Action Buttons Layout
        btn_grid = tk.Frame(action_card, bg="#1e1e2e")
        btn_grid.pack(fill="x", padx=15, pady=10)

        # Task 1: Compact All Instances
        f1 = tk.Frame(btn_grid, bg="#1e1e2e")
        f1.grid(row=0, column=0, sticky="ew", padx=10, pady=5)
        btn_grid.columnconfigure(0, weight=1)
        tk.Label(f1, text="🗜️ Compress All Instances", font=("Segoe UI", 10, "bold"), bg="#1e1e2e", fg="#cdd6f4").pack(anchor="w")
        tk.Label(f1, text="Shrink .vmdk images via NTFS compression (Preserves accounts & data).", font=("Segoe UI", 8), bg="#1e1e2e", fg="#a6adc8").pack(anchor="w")
        ttk.Button(f1, text="Run Disk Compactor", style="Primary.TButton", command=self.start_compress_instances).pack(anchor="w", pady=(6, 0))

        # Task 2: Map Shared Pictures Folder
        f2 = tk.Frame(btn_grid, bg="#1e1e2e")
        f2.grid(row=0, column=1, sticky="ew", padx=10, pady=5)
        btn_grid.columnconfigure(1, weight=1)
        tk.Label(f2, text="📁 Map LD1-85 Pictures Folders", font=("Segoe UI", 10, "bold"), bg="#1e1e2e", fg="#cdd6f4").pack(anchor="w")
        tk.Label(f2, text="Create Desktop LD folders & link /sdcard/Pictures to LD{x}/Pictures.", font=("Segoe UI", 8), bg="#1e1e2e", fg="#a6adc8").pack(anchor="w")
        ttk.Button(f2, text="Create & Map Folders", style="Action.TButton", command=self.start_map_folders).pack(anchor="w", pady=(6, 0))

        # Task 3: Clean AppData Temp
        f3 = tk.Frame(btn_grid, bg="#1e1e2e")
        f3.grid(row=1, column=0, sticky="ew", padx=10, pady=(15, 5))
        tk.Label(f3, text="🧹 Clean AppData Temp", font=("Segoe UI", 10, "bold"), bg="#1e1e2e", fg="#cdd6f4").pack(anchor="w")
        tk.Label(f3, text="Remove leftover Visual Studio setup files and junk cache (30+ GB).", font=("Segoe UI", 8), bg="#1e1e2e", fg="#a6adc8").pack(anchor="w")
        ttk.Button(f3, text="Wipe Temp Files", style="Action.TButton", command=self.start_clean_temp).pack(anchor="w", pady=(6, 0))

        # Task 4: Stop all LD Instances
        f4 = tk.Frame(btn_grid, bg="#1e1e2e")
        f4.grid(row=1, column=1, sticky="ew", padx=10, pady=(15, 5))
        tk.Label(f4, text="🛑 Graceful Shutdown", font=("Segoe UI", 10, "bold"), bg="#1e1e2e", fg="#cdd6f4").pack(anchor="w")
        tk.Label(f4, text="Close all background LDPlayer instances before operations.", font=("Segoe UI", 8), bg="#1e1e2e", fg="#a6adc8").pack(anchor="w")
        ttk.Button(f4, text="Stop All LD Instances", style="Action.TButton", command=self.stop_all_instances).pack(anchor="w", pady=(6, 0))

        # Progress Section
        prog_frame = tk.Frame(main_container, bg="#181825")
        prog_frame.pack(fill="x", pady=(0, 10))

        self.progress_bar = ttk.Progressbar(prog_frame, orient="horizontal", mode="determinate", style="Horizontal.TProgressbar")
        self.progress_bar.pack(fill="x", pady=(0, 5))

        self.status_lbl = tk.Label(prog_frame, textvariable=self.status_var, font=("Segoe UI", 9, "italic"), bg="#181825", fg="#a6adc8")
        self.status_lbl.pack(anchor="w")

        # Real-Time Output Console
        log_frame = tk.Frame(main_container, bg="#11111b")
        log_frame.pack(fill="both", expand=True)

        self.log_text = tk.Text(
            log_frame,
            bg="#11111b",
            fg="#cdd6f4",
            insertbackground="#cdd6f4",
            font=("Consolas", 9),
            relief="flat",
            wrap="word",
        )
        self.log_text.pack(side="left", fill="both", expand=True, padx=10, pady=10)

        scrollbar = ttk.Scrollbar(log_frame, orient="vertical", command=self.log_text.yview)
        scrollbar.pack(side="right", fill="y")
        self.log_text.config(yscrollcommand=scrollbar.set)

    def _create_stat_card(self, parent, title, initial_val, color, col):
        card = ttk.Frame(parent, style="Card.TFrame", padding=(15, 12))
        card.grid(row=0, column=col, sticky="nsew", padx=6)
        parent.columnconfigure(col, weight=1)

        t_lbl = ttk.Label(card, text=title, style="CardSub.TLabel")
        t_lbl.pack(anchor="w")

        val_lbl = tk.Label(card, text=initial_val, font=("Segoe UI", 16, "bold"), bg="#1e1e2e", fg=color)
        val_lbl.pack(anchor="w", pady=(4, 0))
        return val_lbl

    def log(self, message: str):
        def _append():
            self.log_text.insert("end", f"[{time.strftime('%H:%M:%S')}] {message}\n")
            self.log_text.see("end")
        self.after(0, _append)

    def refresh_stats(self):
        def _worker():
            free_gb = get_free_disk_gb("C:")
            vms_dir = os.path.join(self.ld_dir.get(), "vms")
            vmdks = glob.glob(os.path.join(vms_dir, "leidian*", "data.vmdk"))

            total_log = 0
            total_phys = 0
            for vmdk in vmdks:
                l, p = get_file_sizes(vmdk)
                total_log += l
                total_phys += p

            saved_gb = (total_log - total_phys) / (1024**3)

            self.after(0, lambda: self.stat_c_free.config(text=f"{free_gb:.1f} GB"))
            self.after(0, lambda: self.stat_inst_count.config(text=f"{len(vmdks)} VMs"))
            self.after(0, lambda: self.stat_vmdk_saved.config(text=f"+{saved_gb:.1f} GB"))

        threading.Thread(target=_worker, daemon=True).start()

    def stop_all_instances(self):
        ldconsole = os.path.join(self.ld_dir.get(), "ldconsole.exe")
        if not os.path.isfile(ldconsole):
            messagebox.showerror("Error", f"Could not find ldconsole.exe at:\n{ldconsole}")
            return

        def _worker():
            self.log("Stopping all LDPlayer instances...")
            subprocess.run([ldconsole, "quitall"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            time.sleep(2)
            self.log("All instances have been sent quit signal.")
            self.refresh_stats()

        threading.Thread(target=_worker, daemon=True).start()

    def start_compress_instances(self):
        if self.is_busy:
            return
        vms_dir = os.path.join(self.ld_dir.get(), "vms")
        vmdks = glob.glob(os.path.join(vms_dir, "leidian*", "data.vmdk"))
        if not vmdks:
            messagebox.showwarning("Warning", "No instance virtual disks found.")
            return

        vmdks.sort(key=lambda p: os.path.getsize(p), reverse=True)

        def _worker():
            self.is_busy = True
            self.status_var.set("Compressing instances...")
            self.log(f"Starting NTFS compression for {len(vmdks)} instances...")

            # Stop first
            ldconsole = os.path.join(self.ld_dir.get(), "ldconsole.exe")
            if os.path.isfile(ldconsole):
                subprocess.run([ldconsole, "quitall"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                time.sleep(2)

            start_free = get_free_disk_gb("C:")
            for i, vmdk in enumerate(vmdks, start=1):
                name = os.path.basename(os.path.dirname(vmdk))
                pct = int((i / len(vmdks)) * 100)
                self.after(0, lambda p=pct: self.progress_bar.config(value=p))
                self.status_var.set(f"Compressing {name} ({i}/{len(vmdks)})...")
                self.log(f"[{i}/{len(vmdks)}] Compressing {name}...")

                subprocess.run(["compact.exe", "/c", vmdk], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

            end_free = get_free_disk_gb("C:")
            freed = end_free - start_free
            self.log(f"Disk compression finished! Free space on C: {end_free:.1f} GB (+{freed:.1f} GB reclaimed).")
            self.status_var.set("Compression finished!")
            self.progress_bar.config(value=100)
            self.is_busy = False
            self.refresh_stats()
            messagebox.showinfo("Done", f"Finished compressing all {len(vmdks)} instances!\nReclaimed: {freed:.1f} GB.")

        threading.Thread(target=_worker, daemon=True).start()

    def start_map_folders(self):
        if self.is_busy:
            return
        desktop = self.desktop_dir.get()
        config_dir = os.path.join(self.ld_dir.get(), r"vms\config")

        if not os.path.isdir(desktop) or not os.path.isdir(config_dir):
            messagebox.showerror("Error", "Desktop or LDPlayer config directory invalid.")
            return

        def _worker():
            self.is_busy = True
            self.status_var.set("Creating folders and mapping configs...")
            self.log("Creating Pictures subfolders on Desktop (LD1 - LD85)...")

            for i in range(1, 86):
                p = os.path.join(desktop, f"LD{i}", "Pictures")
                os.makedirs(p, exist_ok=True)

            self.log("Mapping config files in LDPlayer...")
            mapped = 0
            for i in range(1, 85):
                cfg = os.path.join(config_dir, f"leidian{i}.config")
                if os.path.isfile(cfg):
                    try:
                        with open(cfg, "r", encoding="utf-8") as f:
                            d = json.load(f)
                        d["statusSettings.sharedPictures"] = f"C:/Users/itxco/Desktop/LD{i}/Pictures"
                        with open(cfg, "w", encoding="utf-8") as f:
                            json.dump(d, f, indent=4)
                        mapped += 1
                    except Exception as e:
                        self.log(f"Error mapping leidian{i}: {e}")

            # Instance 0 (LD)
            cfg0 = os.path.join(config_dir, "leidian0.config")
            if os.path.isfile(cfg0):
                try:
                    with open(cfg0, "r", encoding="utf-8") as f:
                        d0 = json.load(f)
                    d0["statusSettings.sharedPictures"] = "C:/Users/itxco/Desktop/LD85/Pictures"
                    with open(cfg0, "w", encoding="utf-8") as f:
                        json.dump(d0, f, indent=4)
                    mapped += 1
                except Exception as e:
                    self.log(f"Error mapping leidian0: {e}")

            self.log(f"Successfully mapped {mapped} LD instances to Desktop/LDx/Pictures!")
            self.status_var.set("Folder mapping completed!")
            self.is_busy = False
            messagebox.showinfo("Done", f"Created folders and mapped {mapped} LDPlayer instances successfully!")

        threading.Thread(target=_worker, daemon=True).start()

    def start_clean_temp(self):
        if self.is_busy:
            return

        if not messagebox.askyesno("Confirm Cleanup", "Clean temporary files in AppData\\Local\\Temp?\n(Active/in-use files will be safely skipped)."):
            return

        def _worker():
            self.is_busy = True
            self.status_var.set("Cleaning AppData Temp...")
            self.log(f"Starting Temp cleanup in: {TEMP_DIR}...")

            start_free = get_free_disk_gb("C:")
            deleted_dirs = 0
            deleted_files = 0
            skipped = 0

            def _rm_readonly(func, path, exc):
                try:
                    os.chmod(path, stat.S_IWRITE)
                    func(path)
                except Exception:
                    pass

            for entry in os.scandir(TEMP_DIR):
                p = entry.path
                try:
                    if entry.is_dir(follow_symlinks=False):
                        shutil.rmtree(p, onerror=_rm_readonly)
                        deleted_dirs += 1
                    else:
                        os.chmod(p, stat.S_IWRITE)
                        os.remove(p)
                        deleted_files += 1
                except Exception:
                    skipped += 1

            end_free = get_free_disk_gb("C:")
            freed = end_free - start_free
            self.log(f"Temp cleanup completed! Removed {deleted_dirs} dirs, {deleted_files} files. Reclaimed: {freed:.2f} GB.")
            self.status_var.set("Temp cleanup completed!")
            self.is_busy = False
            self.refresh_stats()
            messagebox.showinfo("Done", f"Cleaned AppData Temp!\nFreed: {freed:.2f} GB.")

        threading.Thread(target=_worker, daemon=True).start()


if __name__ == "__main__":
    app = LDOptimizerApp()
    app.mainloop()
