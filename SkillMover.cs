using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SkillMover
{
    [Flags]
    internal enum FileOpenOptions : uint
    {
        PickFolders = 0x00000020,
        ForceFileSystem = 0x00000040,
        PathMustExist = 0x00000800,
        NoChangeDirectory = 0x00000008
    }

    internal enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(FileOpenOptions options);
        void GetOptions(out FileOpenOptions options);
        void SetDefaultFolder(IShellItem shellItem);
        void SetFolder(IShellItem shellItem);
        void GetFolder(out IShellItem shellItem);
        void GetCurrentSelection(out IShellItem shellItem);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem shellItem);
        void AddPlace(IShellItem shellItem, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(ShellItemDisplayName displayName, out IntPtr name);
        void GetAttributes(uint attributes, out uint result);
        void Compare(IShellItem shellItem, uint hint, out int order);
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    internal class FileOpenDialogCom
    {
    }

    internal static class ModernFolderPicker
    {
        private const int CancelledHResult = unchecked((int)0x800704C7);

        public static string Show(IWin32Window owner, string title, string initialPath)
        {
            IFileDialog dialog = null;
            IShellItem result = null;
            try
            {
                dialog = (IFileDialog)new FileOpenDialogCom();
                FileOpenOptions options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options |
                    FileOpenOptions.PickFolders |
                    FileOpenOptions.ForceFileSystem |
                    FileOpenOptions.PathMustExist |
                    FileOpenOptions.NoChangeDirectory);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("选择文件夹");

                SetInitialFolder(dialog, initialPath);

                int showResult = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (showResult == CancelledHResult)
                    return null;
                Marshal.ThrowExceptionForHR(showResult);

                dialog.GetResult(out result);
                IntPtr pathPointer;
                result.GetDisplayName(ShellItemDisplayName.FileSystemPath, out pathPointer);
                try
                {
                    return Marshal.PtrToStringUni(pathPointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
            finally
            {
                if (result != null)
                    Marshal.ReleaseComObject(result);
                if (dialog != null)
                    Marshal.ReleaseComObject(dialog);
            }
        }

        private static void SetInitialFolder(IFileDialog dialog, string initialPath)
        {
            if (string.IsNullOrWhiteSpace(initialPath) || !Directory.Exists(initialPath))
                return;

            IShellItem initialFolder = null;
            try
            {
                Guid shellItemId = typeof(IShellItem).GUID;
                int result = SHCreateItemFromParsingName(
                    Path.GetFullPath(initialPath),
                    IntPtr.Zero,
                    ref shellItemId,
                    out initialFolder);
                if (result >= 0 && initialFolder != null)
                    dialog.SetFolder(initialFolder);
            }
            finally
            {
                if (initialFolder != null)
                    Marshal.ReleaseComObject(initialFolder);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            ref Guid interfaceId,
            out IShellItem shellItem);
    }

    internal sealed class SettingsData
    {
        public List<string> destinations { get; set; }
    }

    internal sealed class CopyResult
    {
        public int DestinationCount { get; set; }
        public int FileCount { get; set; }
        public int CopiedFileCount { get; set; }
        public bool IsSingleFile { get; set; }
        public List<string> SkippedItems { get; set; }
    }

    internal sealed class CopyPlan
    {
        public List<string> Files { get; private set; }
        public List<string> Directories { get; private set; }
        public List<string> SkippedItems { get; private set; }

        public CopyPlan()
        {
            Files = new List<string>();
            Directories = new List<string>();
            SkippedItems = new List<string>();
        }
    }

    internal sealed class CopyProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string FileName { get; set; }
    }

    internal sealed class MainForm : Form
    {
        private static readonly HashSet<string> ExcludedDirectoryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                ".svn",
                ".hg",
                ".idea",
                ".vs",
                ".vscode",
                "__pycache__",
                "node_modules"
            };

        private static readonly HashSet<string> ExcludedFileNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".DS_Store",
                "Thumbs.db",
                "desktop.ini"
            };

        private readonly Color PageBackground = Color.FromArgb(244, 246, 248);
        private readonly Color TextColor = Color.FromArgb(23, 33, 43);
        private readonly Color MutedColor = Color.FromArgb(102, 112, 133);
        private readonly Color AccentColor = Color.FromArgb(37, 99, 235);

        private readonly TextBox sourceTextBox;
        private readonly ComboBox sourceTypeComboBox;
        private readonly Button sourceButton;
        private readonly Button addButton;
        private readonly Button removeButton;
        private readonly Button moveButton;
        private readonly ListBox destinationList;
        private readonly ProgressBar progressBar;
        private readonly Label statusLabel;
        private readonly BackgroundWorker worker;
        private readonly string configPath;

        public MainForm()
        {
            Text = "skill搬运工";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(740, 560);
            ClientSize = new Size(820, 620);
            BackColor = PageBackground;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configPath = Path.Combine(appData, "skill搬运工", "config.json");

            Label title = new Label();
            title.Text = "skill搬运工";
            title.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
            title.ForeColor = TextColor;
            title.AutoSize = true;
            title.Location = new Point(32, 25);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "把一个文件或完整文件夹一次分发到多个位置";
            subtitle.Font = new Font("Microsoft YaHei UI", 10F);
            subtitle.ForeColor = MutedColor;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(34, 70);
            Controls.Add(subtitle);

            Panel card = new Panel();
            card.BackColor = Color.White;
            card.Location = new Point(32, 105);
            card.Size = new Size(756, 482);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(card);

            Label sourceLabel = SectionLabel("1. 选择源内容", new Point(22, 20));
            card.Controls.Add(sourceLabel);

            sourceTextBox = new TextBox();
            sourceTextBox.Font = new Font("Microsoft YaHei UI", 10F);
            sourceTextBox.Location = new Point(22, 53);
            sourceTextBox.Size = new Size(467, 30);
            sourceTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(sourceTextBox);

            sourceTypeComboBox = new ComboBox();
            sourceTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceTypeComboBox.Font = new Font("Microsoft YaHei UI", 10F);
            sourceTypeComboBox.Items.AddRange(new object[] { "文件夹", "文件" });
            sourceTypeComboBox.SelectedIndex = 0;
            sourceTypeComboBox.Location = new Point(499, 51);
            sourceTypeComboBox.Size = new Size(128, 30);
            sourceTypeComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sourceTypeComboBox.SelectedIndexChanged += SourceTypeChanged;
            card.Controls.Add(sourceTypeComboBox);

            sourceButton = SecondaryButton("浏览...");
            sourceButton.Location = new Point(638, 50);
            sourceButton.Size = new Size(96, 34);
            sourceButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sourceButton.Click += ChooseSource;
            card.Controls.Add(sourceButton);

            Label targetLabel = SectionLabel("2. 添加目标文件夹", new Point(22, 105));
            card.Controls.Add(targetLabel);

            removeButton = SecondaryButton("移除所选");
            removeButton.Location = new Point(626, 99);
            removeButton.Size = new Size(108, 34);
            removeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            removeButton.Click += RemoveSelected;
            card.Controls.Add(removeButton);

            addButton = SecondaryButton("添加目标");
            addButton.Location = new Point(516, 99);
            addButton.Size = new Size(100, 34);
            addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addButton.Click += AddDestination;
            card.Controls.Add(addButton);

            Label hint = new Label();
            hint.Text = "目标路径会自动保存。文件保留原名，文件夹保留完整目录结构。";
            hint.Font = new Font("Microsoft YaHei UI", 9F);
            hint.ForeColor = MutedColor;
            hint.AutoSize = true;
            hint.Location = new Point(22, 141);
            card.Controls.Add(hint);

            destinationList = new ListBox();
            destinationList.Font = new Font("Microsoft YaHei UI", 10F);
            destinationList.SelectionMode = SelectionMode.MultiExtended;
            destinationList.BorderStyle = BorderStyle.FixedSingle;
            destinationList.BackColor = Color.FromArgb(250, 251, 252);
            destinationList.ForeColor = TextColor;
            destinationList.Location = new Point(22, 168);
            destinationList.Size = new Size(712, 205);
            destinationList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(destinationList);

            progressBar = new ProgressBar();
            progressBar.Location = new Point(22, 393);
            progressBar.Size = new Size(712, 9);
            progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Style = ProgressBarStyle.Continuous;
            card.Controls.Add(progressBar);

            statusLabel = new Label();
            statusLabel.Text = "请选择要分发的源文件或文件夹";
            statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
            statusLabel.ForeColor = MutedColor;
            statusLabel.AutoEllipsis = true;
            statusLabel.Location = new Point(22, 420);
            statusLabel.Size = new Size(530, 32);
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(statusLabel);

            moveButton = new Button();
            moveButton.Text = "开始搬运";
            moveButton.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            moveButton.ForeColor = Color.White;
            moveButton.BackColor = AccentColor;
            moveButton.FlatStyle = FlatStyle.Flat;
            moveButton.FlatAppearance.BorderSize = 0;
            moveButton.Location = new Point(586, 414);
            moveButton.Size = new Size(148, 46);
            moveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            moveButton.Cursor = Cursors.Hand;
            moveButton.Click += StartCopy;
            card.Controls.Add(moveButton);

            worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += CopyWorker;
            worker.ProgressChanged += CopyProgressChanged;
            worker.RunWorkerCompleted += CopyCompleted;

            LoadDestinations();
        }

        private Label SectionLabel(string text, Point location)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            label.ForeColor = TextColor;
            label.AutoSize = true;
            label.Location = location;
            return label;
        }

        private Button SecondaryButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 9F);
            button.FlatStyle = FlatStyle.System;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void ChooseSource(object sender, EventArgs e)
        {
            bool selectingFile = IsFileMode;
            string selected = selectingFile
                ? SelectFile("选择要分发的源文件", sourceTextBox.Text.Trim())
                : SelectFolder("选择要搬运的源文件夹", sourceTextBox.Text.Trim(), false);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                sourceTextBox.Text = Path.GetFullPath(selected);
                statusLabel.Text = selectingFile
                    ? "源文件已选择，请确认目标文件夹"
                    : "源文件夹已选择，请确认目标文件夹";
            }
        }

        private bool IsFileMode
        {
            get { return sourceTypeComboBox.SelectedIndex == 1; }
        }

        private void SourceTypeChanged(object sender, EventArgs e)
        {
            sourceTextBox.Clear();
            statusLabel.Text = IsFileMode
                ? "当前分发类型：文件，请选择一个源文件"
                : "当前分发类型：文件夹，请选择一个源文件夹";
        }

        private string SelectFile(string title, string initialPath)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = "所有文件 (*.*)|*.*";
                dialog.FilterIndex = 1;
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.Multiselect = false;
                dialog.AutoUpgradeEnabled = true;

                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    if (File.Exists(initialPath))
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(initialPath);
                        dialog.FileName = Path.GetFileName(initialPath);
                    }
                    else if (Directory.Exists(initialPath))
                    {
                        dialog.InitialDirectory = initialPath;
                    }
                }

                return dialog.ShowDialog(this) == DialogResult.OK
                    ? dialog.FileName
                    : null;
            }
        }

        private void AddDestination(object sender, EventArgs e)
        {
            string initialPath = destinationList.Items.Count > 0
                ? destinationList.Items[destinationList.Items.Count - 1].ToString()
                : null;
            string selected = SelectFolder("添加目标文件夹", initialPath, true);
            if (string.IsNullOrWhiteSpace(selected))
                return;

            string path = Path.GetFullPath(selected);
            if (ContainsDestination(path))
            {
                MessageBox.Show(this, "这个目标文件夹已经添加过了。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            destinationList.Items.Add(path);
            SaveDestinations();
            statusLabel.Text = "已添加 " + destinationList.Items.Count + " 个目标文件夹";
        }

        private string SelectFolder(string title, string initialPath, bool allowNewFolder)
        {
            try
            {
                return ModernFolderPicker.Show(this, title, initialPath);
            }
            catch (COMException)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = title;
                    dialog.ShowNewFolderButton = allowNewFolder;
                    if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                        dialog.SelectedPath = initialPath;
                    return dialog.ShowDialog(this) == DialogResult.OK
                        ? dialog.SelectedPath
                        : null;
                }
            }
        }

        private bool ContainsDestination(string path)
        {
            foreach (object item in destinationList.Items)
            {
                if (string.Equals(item.ToString(), path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            List<object> selected = destinationList.SelectedItems.Cast<object>().ToList();
            foreach (object item in selected)
                destinationList.Items.Remove(item);

            SaveDestinations();
            statusLabel.Text = "当前有 " + destinationList.Items.Count + " 个目标文件夹";
        }

        private void LoadDestinations()
        {
            if (!File.Exists(configPath))
                return;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                SettingsData data = serializer.Deserialize<SettingsData>(
                    File.ReadAllText(configPath, Encoding.UTF8));
                if (data == null || data.destinations == null)
                    return;

                foreach (string destination in data.destinations)
                {
                    if (!string.IsNullOrWhiteSpace(destination) && !ContainsDestination(destination))
                        destinationList.Items.Add(destination);
                }

                if (destinationList.Items.Count > 0)
                    statusLabel.Text = "已恢复 " + destinationList.Items.Count + " 个目标文件夹";
            }
            catch
            {
                statusLabel.Text = "目标路径配置无法读取，可重新添加";
            }
        }

        private void SaveDestinations()
        {
            try
            {
                string directory = Path.GetDirectoryName(configPath);
                Directory.CreateDirectory(directory);
                SettingsData data = new SettingsData();
                data.destinations = destinationList.Items.Cast<object>()
                    .Select(item => item.ToString()).ToList();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(configPath, serializer.Serialize(data), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "目标路径暂时无法保存：\r\n" + ex.Message, "保存路径失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void StartCopy(object sender, EventArgs e)
        {
            string source = sourceTextBox.Text.Trim();
            bool sourceIsFile = IsFileMode;
            List<string> destinations = destinationList.Items.Cast<object>()
                .Select(item => item.ToString()).ToList();

            try
            {
                ValidateJob(source, destinations, sourceIsFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "还差一步",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string sourceName = sourceIsFile
                ? Path.GetFileName(source)
                : new DirectoryInfo(source).Name;
            DialogResult result = MessageBox.Show(this,
                "将“" + sourceName + "”复制到 " +
                destinations.Count + " 个目标文件夹。\r\n\r\n" +
                "目标中已有的同名文件会被覆盖，其他文件会保留。是否继续？",
                "确认搬运", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result != DialogResult.OK)
                return;

            SetCopying(true);
            progressBar.Value = 0;
            statusLabel.Text = "正在统计文件并准备搬运...";
            worker.RunWorkerAsync(new object[] { source, destinations, sourceIsFile });
        }

        private static void ValidateJob(
            string source,
            IList<string> destinations,
            bool sourceIsFile)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                (sourceIsFile ? !File.Exists(source) : !Directory.Exists(source)))
            {
                throw new InvalidOperationException(
                    sourceIsFile ? "请选择一个有效的源文件。" : "请选择一个有效的源文件夹。");
            }
            if (destinations.Count == 0)
                throw new InvalidOperationException("请至少添加一个目标文件夹。");

            string sourceFull = sourceIsFile
                ? Path.GetFullPath(source)
                : NormalizeDirectory(source);
            foreach (string destination in destinations)
            {
                if (!Directory.Exists(destination))
                    throw new InvalidOperationException("目标文件夹不存在：" + destination);

                string destinationFull = NormalizeDirectory(destination);
                if (sourceIsFile)
                {
                    string targetFile = Path.Combine(destinationFull, Path.GetFileName(sourceFull));
                    if (string.Equals(sourceFull, targetFile, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("目标文件与源文件相同：" + destination);
                    continue;
                }

                if (string.Equals(sourceFull, destinationFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("目标文件夹不能与源文件夹相同。");
                if (IsInside(destinationFull, sourceFull))
                    throw new InvalidOperationException("目标文件夹不能位于源文件夹内部：" + destination);

                string finalTarget = NormalizeDirectory(
                    Path.Combine(destination, new DirectoryInfo(source).Name));
                if (IsInside(sourceFull, finalTarget))
                    throw new InvalidOperationException("源文件夹不能位于最终目标目录内部：" + destination);
            }
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static bool IsInside(string candidate, string parent)
        {
            string prefix = parent.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private void CopyWorker(object sender, DoWorkEventArgs e)
        {
            object[] arguments = (object[])e.Argument;
            string source = (string)arguments[0];
            List<string> destinations = (List<string>)arguments[1];
            bool sourceIsFile = (bool)arguments[2];

            if (sourceIsFile)
            {
                CopySingleFile(source, destinations, e);
                return;
            }

            CopyPlan plan = BuildCopyPlan(source);
            int total = plan.Files.Count * destinations.Count;
            int copied = 0;

            foreach (string destination in destinations)
            {
                string finalTarget = Path.Combine(destination, new DirectoryInfo(source).Name);
                CopyDirectoryTree(source, finalTarget, plan.Directories, plan.SkippedItems);

                foreach (string file in plan.Files)
                {
                    string relative = file.Substring(source.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string targetFile = Path.Combine(finalTarget, relative);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                        File.Copy(file, targetFile, true);
                        File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(file));
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        plan.SkippedItems.Add(
                            relative + " -> " + destination + "：" + ex.Message);
                    }

                    int percentage = total == 0 ? 100 : (int)((long)copied * 100 / total);
                    worker.ReportProgress(percentage, new CopyProgress
                    {
                        Current = copied,
                        Total = total,
                        FileName = Path.GetFileName(file)
                    });
                }

                try
                {
                    Directory.SetLastWriteTimeUtc(finalTarget, Directory.GetLastWriteTimeUtc(source));
                }
                catch (Exception ex)
                {
                    plan.SkippedItems.Add(
                        new DirectoryInfo(source).Name + " -> " + destination + "：" + ex.Message);
                }
            }

            e.Result = new CopyResult
            {
                DestinationCount = destinations.Count,
                FileCount = plan.Files.Count,
                CopiedFileCount = copied,
                IsSingleFile = false,
                SkippedItems = plan.SkippedItems
            };
        }

        private void CopySingleFile(string source, List<string> destinations, DoWorkEventArgs e)
        {
            int copied = CopySingleFileCore(
                source,
                destinations,
                delegate(int current, int total, string fileName)
                {
                    int percentage = (int)((long)current * 100 / total);
                    worker.ReportProgress(percentage, new CopyProgress
                    {
                        Current = current,
                        Total = total,
                        FileName = fileName
                    });
                });

            e.Result = new CopyResult
            {
                DestinationCount = destinations.Count,
                FileCount = 1,
                CopiedFileCount = copied,
                IsSingleFile = true,
                SkippedItems = new List<string>()
            };
        }

        private static int CopySingleFileCore(
            string source,
            IList<string> destinations,
            Action<int, int, string> progress)
        {
            int copied = 0;
            foreach (string destination in destinations)
            {
                string targetFile = Path.Combine(destination, Path.GetFileName(source));
                File.Copy(source, targetFile, true);
                File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(source));
                copied++;
                if (progress != null)
                    progress(copied, destinations.Count, Path.GetFileName(source));
            }
            return copied;
        }

        private static CopyPlan BuildCopyPlan(string source)
        {
            CopyPlan plan = new CopyPlan();
            Stack<string> pending = new Stack<string>();
            pending.Push(source);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(current);
                }
                catch (Exception ex)
                {
                    plan.SkippedItems.Add(GetRelativePath(source, current) + "：" + ex.Message);
                    continue;
                }

                foreach (string directory in directories)
                {
                    string name = Path.GetFileName(directory);
                    if (ExcludedDirectoryNames.Contains(name))
                    {
                        plan.SkippedItems.Add(GetRelativePath(source, directory) + "（已排除）");
                        continue;
                    }

                    plan.Directories.Add(directory);
                    pending.Push(directory);
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch (Exception ex)
                {
                    plan.SkippedItems.Add(GetRelativePath(source, current) + "：" + ex.Message);
                    continue;
                }

                foreach (string file in files)
                {
                    if (ExcludedFileNames.Contains(Path.GetFileName(file)))
                    {
                        plan.SkippedItems.Add(GetRelativePath(source, file) + "（已排除）");
                        continue;
                    }
                    plan.Files.Add(file);
                }
            }

            return plan;
        }

        private static void CopyDirectoryTree(
            string source,
            string target,
            IList<string> directories,
            IList<string> skippedItems)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in directories)
            {
                string relative = GetRelativePath(source, directory);
                try
                {
                    Directory.CreateDirectory(Path.Combine(target, relative));
                }
                catch (Exception ex)
                {
                    skippedItems.Add(relative + "：" + ex.Message);
                }
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
                return new DirectoryInfo(root).Name;
            return path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void CopyProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
            CopyProgress progress = (CopyProgress)e.UserState;
            statusLabel.Text = "正在搬运 " + progress.Current + "/" + progress.Total +
                "：" + progress.FileName;
        }

        private void CopyCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetCopying(false);
            if (e.Error != null)
            {
                progressBar.Value = 0;
                statusLabel.Text = "搬运未完成，请检查提示后重试";
                MessageBox.Show(this, e.Error.Message, "搬运失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CopyResult result = (CopyResult)e.Result;
            progressBar.Value = 100;
            int skippedCount = result.SkippedItems == null ? 0 : result.SkippedItems.Count;
            statusLabel.Text = skippedCount > 0
                ? "搬运完成，已跳过 " + skippedCount + " 项"
                : result.IsSingleFile
                    ? "分发完成：1 个文件 × " + result.DestinationCount + " 个目标"
                    : "搬运完成：" + result.FileCount + " 个文件 × " +
                        result.DestinationCount + " 个目标";

            string message =
                (result.IsSingleFile ? "已将文件分发到 " : "已将完整文件夹复制到 ") +
                result.DestinationCount + " 个目标位置。\r\n" +
                "源文件数：" + result.FileCount + "\r\n" +
                "完成复制：" + result.CopiedFileCount + " 个文件";

            if (skippedCount > 0)
            {
                string details = string.Join(
                    "\r\n",
                    result.SkippedItems.Take(8).Select(item => "• " + item).ToArray());
                if (skippedCount > 8)
                    details += "\r\n• 另有 " + (skippedCount - 8) + " 项";
                message += "\r\n跳过：" + skippedCount + " 项\r\n\r\n" + details;
            }

            MessageBox.Show(
                this,
                message,
                skippedCount > 0 ? "搬运完成（有跳过项）" : "搬运完成",
                MessageBoxButtons.OK,
                skippedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void SetCopying(bool copying)
        {
            sourceTextBox.Enabled = !copying;
            sourceTypeComboBox.Enabled = !copying;
            sourceButton.Enabled = !copying;
            addButton.Enabled = !copying;
            removeButton.Enabled = !copying;
            destinationList.Enabled = !copying;
            moveButton.Enabled = !copying;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
