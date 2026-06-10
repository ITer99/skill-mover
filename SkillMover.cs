using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SkillMover
{
    internal sealed class SettingsData
    {
        public List<string> destinations { get; set; }
    }

    internal sealed class CopyResult
    {
        public int DestinationCount { get; set; }
        public int FileCount { get; set; }
        public int CopiedFileCount { get; set; }
    }

    internal sealed class CopyProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string FileName { get; set; }
    }

    internal sealed class MainForm : Form
    {
        private readonly Color PageBackground = Color.FromArgb(244, 246, 248);
        private readonly Color TextColor = Color.FromArgb(23, 33, 43);
        private readonly Color MutedColor = Color.FromArgb(102, 112, 133);
        private readonly Color AccentColor = Color.FromArgb(37, 99, 235);

        private readonly TextBox sourceTextBox;
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
            subtitle.Text = "把一个完整文件夹（含全部子目录）一次复制到多个位置";
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

            Label sourceLabel = SectionLabel("1. 选择源文件夹", new Point(22, 20));
            card.Controls.Add(sourceLabel);

            sourceTextBox = new TextBox();
            sourceTextBox.Font = new Font("Microsoft YaHei UI", 10F);
            sourceTextBox.Location = new Point(22, 53);
            sourceTextBox.Size = new Size(605, 30);
            sourceTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(sourceTextBox);

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
            hint.Text = "目标路径会自动保存。源文件夹将以原文件夹名称复制到每个目标中。";
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
            statusLabel.Text = "请选择要搬运的源文件夹";
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
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择要搬运的源文件夹";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    sourceTextBox.Text = Path.GetFullPath(dialog.SelectedPath);
                    statusLabel.Text = "源文件夹已选择，请确认目标文件夹";
                }
            }
        }

        private void AddDestination(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "添加目标文件夹";
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string path = Path.GetFullPath(dialog.SelectedPath);
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
            List<string> destinations = destinationList.Items.Cast<object>()
                .Select(item => item.ToString()).ToList();

            try
            {
                ValidateJob(source, destinations);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "还差一步",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult result = MessageBox.Show(this,
                "将“" + new DirectoryInfo(source).Name + "”完整复制到 " +
                destinations.Count + " 个目标文件夹。\r\n\r\n" +
                "目标中已有的同名文件会被覆盖，其他文件会保留。是否继续？",
                "确认搬运", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result != DialogResult.OK)
                return;

            SetCopying(true);
            progressBar.Value = 0;
            statusLabel.Text = "正在统计文件并准备搬运...";
            worker.RunWorkerAsync(new object[] { source, destinations });
        }

        private static void ValidateJob(string source, IList<string> destinations)
        {
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                throw new InvalidOperationException("请选择一个有效的源文件夹。");
            if (destinations.Count == 0)
                throw new InvalidOperationException("请至少添加一个目标文件夹。");

            string sourceFull = NormalizeDirectory(source);
            foreach (string destination in destinations)
            {
                if (!Directory.Exists(destination))
                    throw new InvalidOperationException("目标文件夹不存在：" + destination);

                string destinationFull = NormalizeDirectory(destination);
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
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            int total = files.Length * destinations.Count;
            int copied = 0;

            foreach (string destination in destinations)
            {
                string finalTarget = Path.Combine(destination, new DirectoryInfo(source).Name);
                CopyDirectoryTree(source, finalTarget);

                foreach (string file in files)
                {
                    string relative = file.Substring(source.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string targetFile = Path.Combine(finalTarget, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                    File.Copy(file, targetFile, true);
                    File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(file));
                    copied++;
                    int percentage = total == 0 ? 100 : (int)((long)copied * 100 / total);
                    worker.ReportProgress(percentage, new CopyProgress
                    {
                        Current = copied,
                        Total = total,
                        FileName = Path.GetFileName(file)
                    });
                }

                Directory.SetLastWriteTimeUtc(finalTarget, Directory.GetLastWriteTimeUtc(source));
            }

            e.Result = new CopyResult
            {
                DestinationCount = destinations.Count,
                FileCount = files.Length,
                CopiedFileCount = copied
            };
        }

        private static void CopyDirectoryTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(target, relative));
            }
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
            statusLabel.Text = "搬运完成：" + result.FileCount + " 个文件 × " +
                result.DestinationCount + " 个目标";
            MessageBox.Show(this,
                "已将完整文件夹复制到 " + result.DestinationCount + " 个目标位置。\r\n" +
                "源文件数：" + result.FileCount + "\r\n" +
                "完成复制：" + result.CopiedFileCount + " 个文件",
                "搬运完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetCopying(bool copying)
        {
            sourceTextBox.Enabled = !copying;
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
