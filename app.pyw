from __future__ import annotations

import queue
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from copy_engine import CopySummary, copy_folder_to_many, normalize_path
from settings_store import SettingsStore


class SkillMoverApp:
    BG = "#F4F6F8"
    CARD = "#FFFFFF"
    TEXT = "#17212B"
    MUTED = "#667085"
    ACCENT = "#2563EB"
    ACCENT_ACTIVE = "#1D4ED8"
    BORDER = "#D9E0E7"

    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("skill搬运工")
        self.root.geometry("820x620")
        self.root.minsize(720, 540)
        self.root.configure(bg=self.BG)

        self.source_var = tk.StringVar()
        self.status_var = tk.StringVar(value="请选择要搬运的源文件夹")
        self.progress_var = tk.DoubleVar(value=0)
        self.events: queue.Queue[tuple[str, object]] = queue.Queue()
        self.is_copying = False
        self.settings = SettingsStore()

        self._configure_styles()
        self._build_ui()
        self._load_saved_destinations()
        self.root.after(100, self._process_events)

    def _configure_styles(self) -> None:
        style = ttk.Style()
        style.theme_use("vista")
        style.configure("TFrame", background=self.BG)
        style.configure("Card.TFrame", background=self.CARD)
        style.configure(
            "Title.TLabel",
            background=self.BG,
            foreground=self.TEXT,
            font=("Microsoft YaHei UI", 22, "bold"),
        )
        style.configure(
            "Subtitle.TLabel",
            background=self.BG,
            foreground=self.MUTED,
            font=("Microsoft YaHei UI", 10),
        )
        style.configure(
            "Section.TLabel",
            background=self.CARD,
            foreground=self.TEXT,
            font=("Microsoft YaHei UI", 11, "bold"),
        )
        style.configure(
            "Hint.TLabel",
            background=self.CARD,
            foreground=self.MUTED,
            font=("Microsoft YaHei UI", 9),
        )
        style.configure(
            "Status.TLabel",
            background=self.CARD,
            foreground=self.MUTED,
            font=("Microsoft YaHei UI", 9),
        )
        style.configure(
            "Accent.TButton",
            font=("Microsoft YaHei UI", 11, "bold"),
            padding=(24, 11),
        )
        style.configure(
            "Secondary.TButton",
            font=("Microsoft YaHei UI", 9),
            padding=(12, 7),
        )
        style.configure("Mover.Horizontal.TProgressbar", thickness=8)

    def _build_ui(self) -> None:
        container = ttk.Frame(self.root, padding=(32, 26, 32, 28))
        container.pack(fill="both", expand=True)

        ttk.Label(container, text="skill搬运工", style="Title.TLabel").pack(anchor="w")
        ttk.Label(
            container,
            text="把一个完整文件夹（含全部子目录）一次复制到多个位置",
            style="Subtitle.TLabel",
        ).pack(anchor="w", pady=(4, 20))

        card = ttk.Frame(container, style="Card.TFrame", padding=22)
        card.pack(fill="both", expand=True)
        card.columnconfigure(0, weight=1)
        card.rowconfigure(4, weight=1)

        ttk.Label(card, text="1. 选择源文件夹", style="Section.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        source_row = ttk.Frame(card, style="Card.TFrame")
        source_row.grid(row=1, column=0, sticky="ew", pady=(9, 20))
        source_row.columnconfigure(0, weight=1)

        self.source_entry = ttk.Entry(
            source_row,
            textvariable=self.source_var,
            font=("Microsoft YaHei UI", 10),
        )
        self.source_entry.grid(row=0, column=0, sticky="ew", ipady=6)
        self.source_button = ttk.Button(
            source_row,
            text="浏览...",
            style="Secondary.TButton",
            command=self._choose_source,
        )
        self.source_button.grid(row=0, column=1, padx=(10, 0))

        target_header = ttk.Frame(card, style="Card.TFrame")
        target_header.grid(row=2, column=0, sticky="ew")
        target_header.columnconfigure(0, weight=1)
        ttk.Label(target_header, text="2. 添加目标文件夹", style="Section.TLabel").grid(
            row=0, column=0, sticky="w"
        )
        self.add_button = ttk.Button(
            target_header,
            text="添加目标",
            style="Secondary.TButton",
            command=self._add_destinations,
        )
        self.add_button.grid(row=0, column=1, padx=(8, 0))
        self.remove_button = ttk.Button(
            target_header,
            text="移除所选",
            style="Secondary.TButton",
            command=self._remove_selected,
        )
        self.remove_button.grid(row=0, column=2, padx=(8, 0))

        ttk.Label(
            card,
            text="可重复点击“添加目标”，源文件夹会以原文件夹名称复制到每个目标中。",
            style="Hint.TLabel",
        ).grid(row=3, column=0, sticky="w", pady=(6, 8))

        list_frame = ttk.Frame(card, style="Card.TFrame")
        list_frame.grid(row=4, column=0, sticky="nsew")
        list_frame.columnconfigure(0, weight=1)
        list_frame.rowconfigure(0, weight=1)

        self.destination_list = tk.Listbox(
            list_frame,
            selectmode=tk.EXTENDED,
            font=("Microsoft YaHei UI", 10),
            bg="#FAFBFC",
            fg=self.TEXT,
            selectbackground="#DCE8FF",
            selectforeground=self.TEXT,
            highlightthickness=1,
            highlightbackground=self.BORDER,
            highlightcolor=self.ACCENT,
            borderwidth=0,
            activestyle="none",
        )
        self.destination_list.grid(row=0, column=0, sticky="nsew")
        scrollbar = ttk.Scrollbar(
            list_frame, orient="vertical", command=self.destination_list.yview
        )
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.destination_list.configure(yscrollcommand=scrollbar.set)

        footer = ttk.Frame(card, style="Card.TFrame")
        footer.grid(row=5, column=0, sticky="ew", pady=(18, 0))
        footer.columnconfigure(0, weight=1)

        self.progress = ttk.Progressbar(
            footer,
            variable=self.progress_var,
            maximum=100,
            style="Mover.Horizontal.TProgressbar",
        )
        self.progress.grid(row=0, column=0, columnspan=2, sticky="ew")
        ttk.Label(footer, textvariable=self.status_var, style="Status.TLabel").grid(
            row=1, column=0, sticky="w", pady=(8, 0)
        )
        self.move_button = ttk.Button(
            footer,
            text="开始搬运",
            style="Accent.TButton",
            command=self._start_copy,
        )
        self.move_button.grid(row=1, column=1, rowspan=2, sticky="e", padx=(18, 0), pady=(8, 0))

    def _choose_source(self) -> None:
        selected = filedialog.askdirectory(title="选择要搬运的源文件夹")
        if selected:
            self.source_var.set(str(normalize_path(selected)))
            self.status_var.set("源文件夹已选择，请添加一个或多个目标文件夹")

    def _add_destinations(self) -> None:
        selected = filedialog.askdirectory(title="添加目标文件夹")
        if not selected:
            return

        normalized = str(normalize_path(selected))
        existing = set(self.destination_list.get(0, tk.END))
        if normalized in existing:
            messagebox.showinfo("提示", "这个目标文件夹已经添加过了。")
            return

        self.destination_list.insert(tk.END, normalized)
        self._save_destinations()
        count = self.destination_list.size()
        self.status_var.set(f"已添加 {count} 个目标文件夹")

    def _remove_selected(self) -> None:
        for index in reversed(self.destination_list.curselection()):
            self.destination_list.delete(index)
        self._save_destinations()
        self.status_var.set(f"当前有 {self.destination_list.size()} 个目标文件夹")

    def _load_saved_destinations(self) -> None:
        destinations = self.settings.load_destinations()
        for destination in destinations:
            self.destination_list.insert(tk.END, destination)
        if destinations:
            self.status_var.set(f"已恢复 {len(destinations)} 个目标文件夹")

    def _save_destinations(self) -> None:
        destinations = self.destination_list.get(0, tk.END)
        try:
            self.settings.save_destinations(destinations)
        except OSError as exc:
            messagebox.showwarning("保存路径失败", f"目标路径暂时无法保存：\n{exc}")

    def _start_copy(self) -> None:
        if self.is_copying:
            return

        source = self.source_var.get().strip()
        destinations = list(self.destination_list.get(0, tk.END))
        if not source:
            messagebox.showwarning("还差一步", "请先选择源文件夹。")
            return
        if not destinations:
            messagebox.showwarning("还差一步", "请至少添加一个目标文件夹。")
            return

        source_name = Path(source).name
        answer = messagebox.askokcancel(
            "确认搬运",
            f"将“{source_name}”完整复制到 {len(destinations)} 个目标文件夹。\n\n"
            "目标中已有的同名文件会被覆盖，其他文件会保留。是否继续？",
        )
        if not answer:
            return

        self._set_copying(True)
        self.progress_var.set(0)
        self.status_var.set("正在统计文件并准备搬运...")
        worker = threading.Thread(
            target=self._copy_worker,
            args=(source, destinations),
            daemon=True,
        )
        worker.start()

    def _copy_worker(self, source: str, destinations: list[str]) -> None:
        try:
            summary = copy_folder_to_many(
                source,
                destinations,
                progress=lambda current, total, src, dst: self.events.put(
                    ("progress", (current, total, src, dst))
                ),
            )
            self.events.put(("done", summary))
        except Exception as exc:
            self.events.put(("error", exc))

    def _process_events(self) -> None:
        try:
            while True:
                event_type, payload = self.events.get_nowait()
                if event_type == "progress":
                    current, total, source_file, _ = payload
                    percent = 100 if total == 0 else current / total * 100
                    self.progress_var.set(percent)
                    self.status_var.set(
                        f"正在搬运 {current}/{total}：{Path(source_file).name}"
                    )
                elif event_type == "done":
                    self._copy_finished(payload)
                elif event_type == "error":
                    self._copy_failed(payload)
        except queue.Empty:
            pass
        finally:
            self.root.after(100, self._process_events)

    def _copy_finished(self, summary: CopySummary) -> None:
        self._set_copying(False)
        self.progress_var.set(100)
        self.status_var.set(
            f"搬运完成：{summary.file_count} 个文件 × {summary.destination_count} 个目标"
        )
        messagebox.showinfo(
            "搬运完成",
            f"已将完整文件夹复制到 {summary.destination_count} 个目标位置。\n"
            f"源文件数：{summary.file_count}\n"
            f"完成复制：{summary.copied_file_count} 个文件",
        )

    def _copy_failed(self, error: Exception) -> None:
        self._set_copying(False)
        self.progress_var.set(0)
        self.status_var.set("搬运未完成，请检查提示后重试")
        messagebox.showerror("搬运失败", str(error))

    def _set_copying(self, copying: bool) -> None:
        self.is_copying = copying
        state = "disabled" if copying else "normal"
        for widget in (
            self.source_entry,
            self.source_button,
            self.add_button,
            self.remove_button,
            self.destination_list,
            self.move_button,
        ):
            widget.configure(state=state)


def main() -> None:
    root = tk.Tk()
    SkillMoverApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
