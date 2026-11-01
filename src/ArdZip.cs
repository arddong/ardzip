// arddong ArdZip = 광고 없는 압축 프로그램(풀기 zip·7z·rar·tar·gz·alz·egg / 만들기 zip)
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text;
using EggDotNet;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ArdZip;

public record Item(string Path, long Size, DateTime? Time, bool IsDir);

public class NeedPasswordException : Exception
{
    public NeedPasswordException(Exception? inner = null) : base("암호가 필요하거나 틀렸습니다", inner) { }
}

// ───────────────────────── 엔진 ─────────────────────────
public static class Engine
{
    static readonly Encoding Cp949;

    static Engine()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static readonly string[] Exts =
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".alz", ".egg" };

    public static bool IsArchive(string p) => File.Exists(p) && Exts.Contains(Path.GetExtension(p).ToLowerInvariant());

    static bool IsEgg(string p) => Path.GetExtension(p).ToLowerInvariant() is ".alz" or ".egg";

    // UTF-8 표시가 없는 zip 이름은 한국 윈도우 기본(cp949)으로 읽는다. 그래야 한글 파일명이 안 깨진다. tar 등은 UTF-8 이 기본이다
    static ReaderOptions Opts(string path, string? pw) => new()
    {
        Password = pw,
        ArchiveEncoding = new ArchiveEncoding { Default = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? Cp949 : Encoding.UTF8 },
    };

    static bool IsPasswordError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is System.Security.Cryptography.CryptographicException or SharpCompress.Common.CryptographicException or NeedPasswordException || e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static EggArchive OpenEgg(Stream fs, string? pw) =>
        new(fs, false, null, (_, o) =>
        {
            if (pw == null) throw new NeedPasswordException();
            o.Password = pw;
            o.Retry = false;
        });

    static bool IsDirName(string? n) => n != null && (n.EndsWith('/') || n.EndsWith('\\'));

    // tar.gz 처럼 통째 스트림으로 감싼 것은 앞에서부터 읽는 길로만 열린다
    static bool IsStreamOnly(string p) => Path.GetExtension(p).ToLowerInvariant() is ".gz" or ".tgz" or ".bz2" or ".tbz2" or ".xz" or ".txz" or ".zst";

    // 옛 alz 는 날짜 칸이 비어 있는 경우가 있어 읽다가 터진다. 날짜만 버리고 계속 간다
    static DateTime? EggTime(EggArchiveEntry e)
    {
        try { return e.LastWriteTime is DateTime t && t.Year >= 1980 ? t : null; } catch { return null; }
    }

    static string Norm(string? p) => (p ?? "").Replace('\\', '/').TrimStart('/');

    public static List<Item> List(string path, string? pw)
    {
        try
        {
            if (IsEgg(path))
            {
                using var fs = File.OpenRead(path);
                using var a = OpenEgg(fs, pw);
                if (pw == null && a.Entries.Any(e => e.IsEncrypted)) throw new NeedPasswordException();
                return a.Entries.Select(e => new Item(Norm(e.FullName), e.UncompressedLength, EggTime(e),
                    IsDirName(e.FullName))).ToList();
            }
            if (IsStreamOnly(path))
            {
                var list = new List<Item>();
                using var r = ReaderFactory.OpenReader(path, Opts(path, pw));
                while (r.MoveToNextEntry()) list.Add(new Item(Norm(r.Entry.Key), r.Entry.Size, r.Entry.LastModifiedTime, r.Entry.IsDirectory));
                return list;
            }
            using var arc = ArchiveFactory.OpenArchive(path, Opts(path, pw));
            var items = arc.Entries.Select(e => new Item(Norm(e.Key ?? ""), e.Size, e.LastModifiedTime, e.IsDirectory)).ToList();
            if (pw == null && arc.Entries.Any(e => e.IsEncrypted)) throw new NeedPasswordException();
            return items;
        }
        catch (Exception ex) when (IsPasswordError(ex) && ex is not NeedPasswordException)
        {
            throw new NeedPasswordException(ex);
        }
    }

    public static string BaseName(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var dbl in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst" })
            if (name.EndsWith(dbl, StringComparison.OrdinalIgnoreCase)) return name[..^dbl.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    public static string Unique(string path, bool isDir)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = isDir ? "" : Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    // 반디집 「자동 풀기」 방식 = 안에 최상위 항목이 하나뿐이면 그 자리에, 여럿이면 압축파일 이름 폴더를 만들어 푼다
    public static string PickDest(string archive, IList<Item> items)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(archive))!;
        var tops = items.Select(i => i.Path.Split('/')[0]).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tops.Count == 1)
        {
            var target = Path.Combine(parent, tops[0]);
            if (!File.Exists(target) && !Directory.Exists(target)) return parent;
        }
        return Unique(Path.Combine(parent, BaseName(archive)), true);
    }

    static string SafeTarget(string root, string entry)
    {
        var full = Path.GetFullPath(Path.Combine(root, entry));
        var rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new IOException("압축 파일 밖으로 나가는 경로가 있어 멈췄습니다: " + entry);
        return full;
    }

    /// <returns>풀린 폴더(또는 그 자리) 경로</returns>
    public static string Extract(string path, string? pw, string? destOverride, IProgress<(int done, int total, string name)>? prog, CancellationToken ct)
    {
        var items = List(path, pw);
        int total = Math.Max(1, items.Count(i => !i.IsDir));
        var dest = destOverride ?? PickDest(path, items);
        bool madeDest = !Directory.Exists(dest);
        Directory.CreateDirectory(dest);
        int done = 0;
        try
        {
            if (IsEgg(path))
            {
                using var fs = File.OpenRead(path);
                using var a = OpenEgg(fs, pw);
                foreach (var e in a.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = SafeTarget(dest, Norm(e.FullName));
                    if (IsDirName(e.FullName)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var src = e.Open())
                    using (var dst = File.Create(target))
                        src.CopyTo(dst);
                    if (EggTime(e) is DateTime t && t.Year >= 1980) File.SetLastWriteTime(target, t);
                    prog?.Report((++done, total, Norm(e.FullName)));
                }
                return dest;
            }

            var opt = new ExtractionOptions { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };
            void One(IEntry entry, Action<string> write)
            {
                ct.ThrowIfCancellationRequested();
                var key = Norm(entry.Key);
                var target = SafeTarget(dest, key);
                if (entry.IsDirectory) { Directory.CreateDirectory(target); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                write(target);
                prog?.Report((++done, total, key));
            }

            if (IsStreamOnly(path))
            {
                using var r = ReaderFactory.OpenReader(path, Opts(path, pw));
                while (r.MoveToNextEntry()) One(r.Entry, t => r.WriteEntryToFile(t, opt));
            }
            else
            {
                using var arc = ArchiveFactory.OpenArchive(path, Opts(path, pw));
                // 7z·통짜(solid) 압축은 앞에서부터 차례로 읽어야 빠르다. 나머지는 항목마다 따로 읽는다
                if (arc.IsSolid || arc.Type == ArchiveType.SevenZip)
                {
                    using var reader = arc.ExtractAllEntries();
                    while (reader.MoveToNextEntry()) One(reader.Entry, t => reader.WriteEntryToFile(t, opt));
                }
                else
                    foreach (var e in arc.Entries) One(e, t => e.WriteToFile(t, opt));
            }
        }
        catch (Exception ex)
        {
            // 도중에 멈추면 이번에 새로 만든 폴더는 지운다. 반쯤 풀린 폴더가 남지 않게
            if (madeDest) try { Directory.Delete(dest, true); } catch { }
            if (IsPasswordError(ex) && ex is not NeedPasswordException) throw new NeedPasswordException(ex);
            throw;
        }
        return dest;
    }

    public static string ZipNameFor(IList<string> paths)
    {
        var first = Path.GetFullPath(paths[0]).TrimEnd('\\');
        var parent = Path.GetDirectoryName(first)!;
        string stem = paths.Count == 1
            ? (Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first))
            : (Path.GetFileName(parent) is { Length: > 0 } p ? p : "압축");
        return Unique(Path.Combine(parent, stem + ".zip"), false);
    }

    public static string CompressZip(IList<string> paths, string? outPath, IProgress<(int done, int total, string name)>? prog, CancellationToken ct)
    {
        outPath ??= ZipNameFor(paths);
        var files = new List<(string src, string name)>();
        var emptyDirs = new List<string>();
        foreach (var raw in paths)
        {
            var p = Path.GetFullPath(raw).TrimEnd('\\');
            var baseDir = Path.GetDirectoryName(p)!;
            if (Directory.Exists(p))
            {
                foreach (var d in Directory.EnumerateDirectories(p, "*", SearchOption.AllDirectories).Prepend(p))
                    if (!Directory.EnumerateFileSystemEntries(d).Any())
                        emptyDirs.Add(Path.GetRelativePath(baseDir, d).Replace('\\', '/') + "/");
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    files.Add((f, Path.GetRelativePath(baseDir, f).Replace('\\', '/')));
            }
            else if (File.Exists(p))
                files.Add((p, Path.GetFileName(p)));
        }
        files.RemoveAll(f => string.Equals(f.src, outPath, StringComparison.OrdinalIgnoreCase));
        try
        {
            // 이름은 UTF-8 로 쓰고 표시도 남긴다. 탐색기·맥·반디집 모두 한글이 안 깨진다
            using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create, Encoding.UTF8);
            foreach (var d in emptyDirs) zip.CreateEntry(d);
            int done = 0;
            foreach (var (src, name) in files)
            {
                ct.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(src, name, CompressionLevel.Optimal);
                prog?.Report((++done, Math.Max(1, files.Count), name));
            }
        }
        catch
        {
            try { File.Delete(outPath); } catch { }
            throw;
        }
        return outPath;
    }
}

// ───────────────────────── 화면 공통 ─────────────────────────
static class Ui
{
    public static readonly Color Bg = Color.FromArgb(250, 250, 251);
    public static readonly Color Ink = Color.FromArgb(32, 33, 36);
    public static readonly Color Sub = Color.FromArgb(110, 113, 120);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color Line = Color.FromArgb(225, 227, 232);
    public static readonly Font Body = new("맑은 고딕", 10f);
    public static readonly Font Title = new("맑은 고딕", 13f, FontStyle.Bold);

    public static Button Btn(string text, bool primary = false)
    {
        var b = new FlatButton
        {
            Text = text, AutoSize = false, Height = 36, Width = 120, FlatStyle = FlatStyle.Flat, Font = Body,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0),
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Line;
        return b;
    }

    public static string? AskPassword(IWin32Window? owner, string file)
    {
        using var f = new Form
        {
            Text = "암호", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(380, 140), BackColor = Bg, Font = Body, TopMost = true,
        };
        var lbl = new Label { Text = Path.GetFileName(file) + " 의 암호", Left = 16, Top = 16, Width = 348, ForeColor = Ink };
        var box = new TextBox { Left = 16, Top = 44, Width = 348, UseSystemPasswordChar = true };
        var ok = Btn("확인", true); ok.Left = 152; ok.Top = 88; ok.DialogResult = DialogResult.OK;
        var no = Btn("취소"); no.Left = 252; no.Top = 88; no.Width = 112; no.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { lbl, box, ok, no });
        f.AcceptButton = ok; f.CancelButton = no;
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }

    public static string Size(long b) => b < 1024 ? $"{b} B" : b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : b < 1 << 30 ? $"{b / 1048576.0:0.#} MB" : $"{b / 1073741824.0:0.##} GB";

    public static Icon? AppIcon => Icon.ExtractAssociatedIcon(Application.ExecutablePath);
}

// 눌렀던 버튼에 검은 포커스 테두리가 남지 않게
class FlatButton : Button
{
    protected override bool ShowFocusCues => false;
}

// ───────────────────────── 진행 창 (오른쪽 클릭으로 부를 때) ─────────────────────────
class JobForm : Form
{
    readonly Label _what = new() { Left = 16, Top = 14, Width = 408, Height = 22, ForeColor = Ui.Ink, AutoEllipsis = true };
    readonly Label _file = new() { Left = 16, Top = 38, Width = 408, Height = 20, ForeColor = Ui.Sub, AutoEllipsis = true };
    readonly ProgressBar _bar = new() { Left = 16, Top = 64, Width = 408, Height = 10, Style = ProgressBarStyle.Continuous };
    readonly CancellationTokenSource _cts = new();
    readonly Func<IProgress<(int, int, string)>, CancellationToken, Func<string?>, string> _work;

    public int ExitCode { get; private set; }

    public JobForm(string title, Func<IProgress<(int, int, string)>, CancellationToken, Func<string?>, string> work)
    {
        _work = work;
        Text = "ArdZip"; Icon = Ui.AppIcon; Font = Ui.Body; BackColor = Ui.Bg;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 128);
        _what.Text = title;
        var cancel = Ui.Btn("취소"); cancel.Left = 324; cancel.Top = 84; cancel.Width = 100; cancel.Height = 32;
        cancel.Click += (_, _) => { _cts.Cancel(); cancel.Enabled = false; };
        Controls.AddRange(new Control[] { _what, _file, _bar, cancel });
        Shown += async (_, _) => await Run();
        FormClosing += (_, _) => _cts.Cancel();
    }

    async Task Run()
    {
        var prog = new Progress<(int done, int total, string name)>(p =>
        {
            _bar.Maximum = p.total; _bar.Value = Math.Min(p.done, p.total); _file.Text = p.name;
        });
        string? Ask() => (string?)Invoke(() => Ui.AskPassword(this, _what.Text));
        try
        {
            await Task.Run(() => _work(prog, _cts.Token, Ask));
        }
        catch (OperationCanceledException) { ExitCode = 2; }
        catch (Exception ex)
        {
            ExitCode = 1;
            MessageBox.Show(this, ex.Message, "ArdZip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Close();
    }
}

// ───────────────────────── 본 창 (끌어다 놓기·목록 보기) ─────────────────────────
class MainForm : Form
{
    readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Ui.Body, BackColor = Color.White, ForeColor = Ui.Ink,
    };
    readonly Label _hint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Sub, Font = new Font("맑은 고딕", 11f),
        Text = "압축 파일을 끌어다 놓으면 안을 보여 줍니다\n\n파일·폴더를 끌어다 놓으면 zip 으로 묶습니다", BackColor = Color.White,
    };
    readonly Label _title = new() { Dock = DockStyle.Fill, Font = Ui.Title, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "ArdZip" };
    readonly Label _status = new() { AutoSize = false, Width = 300, Height = 36, ForeColor = Ui.Sub, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 3, Style = ProgressBarStyle.Continuous, Visible = false };
    readonly Button _open = Ui.Btn("열기"), _extract = Ui.Btn("압축 풀기", true), _extractTo = Ui.Btn("다른 곳에 풀기"), _zip = Ui.Btn("zip 으로 묶기", true);
    string? _archive, _password;
    List<string> _pending = new();

    public MainForm(string? path)
    {
        Text = "ArdZip"; Icon = Ui.AppIcon; Font = Ui.Body; BackColor = Ui.Bg;
        ClientSize = new Size(760, 480); MinimumSize = new Size(560, 360); StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        _list.Columns.Add("이름", 430); _list.Columns.Add("크기", 110, HorizontalAlignment.Right); _list.Columns.Add("수정한 날", 150);
        _list.ClientSizeChanged += (_, _) => _list.Columns[0].Width = Math.Max(200, _list.ClientSize.Width - _list.Columns[1].Width - _list.Columns[2].Width);

        var top = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(20, 8, 20, 0), BackColor = Ui.Bg };
        top.Controls.Add(_title);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 0), BackColor = Ui.Bg };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(Ui.Line), 0, 0, card.Width - 1, card.Height - 1);
        card.Controls.Add(_list); card.Controls.Add(_hint); card.Controls.Add(_bar);
        body.Controls.Add(card);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(20, 14, 12, 0), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Bg,
        };
        bottom.Controls.AddRange(new Control[] { _extract, _zip, _extractTo, _open, _status });

        Controls.Add(body); Controls.Add(bottom); Controls.Add(top);

        _open.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "압축 파일|" + string.Join(";", Engine.Exts.Select(x => "*" + x)) + "|모든 파일|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) Take(new[] { d.FileName });
        };
        _extract.Click += async (_, _) => await DoExtract(null);
        _extractTo.Click += async (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "풀 곳", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK) await DoExtract(d.SelectedPath);
        };
        _zip.Click += async (_, _) => await DoZip();

        foreach (Control c in new Control[] { this, _list, _hint })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) => e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += (_, e) => Take((string[])e.Data!.GetData(DataFormats.FileDrop)!);
        }

        Reset();
        if (path != null) Shown += (_, _) => Take(new[] { path });
    }

    void Reset()
    {
        _archive = null; _password = null; _pending.Clear();
        _list.Items.Clear(); _list.Visible = false; _hint.Visible = true;
        _title.Text = "ArdZip"; _status.Text = "";
        _extract.Visible = _extractTo.Visible = false; _zip.Visible = false;
    }

    void Take(string[] paths)
    {
        if (paths.Length == 1 && Engine.IsArchive(paths[0])) { ShowArchive(paths[0]); return; }
        Reset();
        _pending = paths.ToList();
        _title.Text = paths.Length == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Length}개 항목";
        _hint.Visible = false; _list.Visible = true;
        foreach (var p in paths)
        {
            bool dir = Directory.Exists(p);
            _list.Items.Add(new ListViewItem(new[] { Path.GetFileName(p.TrimEnd('\\')), dir ? "폴더" : Ui.Size(new FileInfo(p).Length), "" }));
        }
        _status.Text = "만들 파일 : " + Path.GetFileName(Engine.ZipNameFor(_pending));
        _zip.Visible = true;
    }

    void ShowArchive(string path)
    {
        Reset();
        List<Item> items;
        string? pw = null;
        while (true)
        {
            try { items = Engine.List(path, pw); break; }
            catch (NeedPasswordException)
            {
                pw = Ui.AskPassword(this, path);
                if (pw == null) return;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ArdZip", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        _archive = path; _password = pw;
        _title.Text = Path.GetFileName(path);
        _list.BeginUpdate();
        foreach (var i in items.Where(i => !i.IsDir).OrderBy(i => i.Path, StringComparer.CurrentCultureIgnoreCase))
            _list.Items.Add(new ListViewItem(new[] { i.Path, Ui.Size(i.Size), i.Time?.ToString("yyyy-MM-dd HH:mm") ?? "" }));
        _list.EndUpdate();
        _hint.Visible = false; _list.Visible = true;
        var files = items.Where(i => !i.IsDir).ToList();
        _status.Text = $"파일 {files.Count}개 · {Ui.Size(files.Sum(i => i.Size))}";
        _extract.Visible = _extractTo.Visible = true;
    }

    async Task Busy(Func<IProgress<(int, int, string)>, string> work, Action<string> done)
    {
        _bar.Visible = true; _bar.Value = 0; Enabled = false;
        var prog = new Progress<(int done, int total, string name)>(p => { _bar.Maximum = p.total; _bar.Value = Math.Min(p.done, p.total); _status.Text = p.name; });
        try { done(await Task.Run(() => work(prog))); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ArdZip", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _bar.Visible = false; Enabled = true; }
    }

    async Task DoExtract(string? dest)
    {
        if (_archive == null) return;
        var a = _archive; var pw = _password;
        await Busy(p => Engine.Extract(a, pw, dest, p, CancellationToken.None), d =>
        {
            _status.Text = "풀었습니다 : " + d;
            Process.Start("explorer.exe", $"\"{d}\"");
        });
    }

    async Task DoZip()
    {
        if (_pending.Count == 0) return;
        var items = _pending.ToList();
        await Busy(p => Engine.CompressZip(items, null, p, CancellationToken.None), z =>
        {
            Reset();
            _status.Text = "만들었습니다 : " + Path.GetFileName(z);
            Process.Start("explorer.exe", $"/select,\"{z}\"");
        });
    }
}

// ───────────────────────── 시작점 ─────────────────────────
static class Program
{
    const string PipeName = "ArdZip_collect";

    // 탐색기는 여러 개를 골라 오른쪽 클릭하면 파일마다 프로그램을 따로 띄운다. 첫 번째가 나머지 이름을 모아 zip 하나로 묶는다
    static List<string>? Collect(string[] mine)
    {
        using var mtx = new Mutex(true, "ArdZip_collect_" + Environment.UserName, out bool first);
        if (!first)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(3000);
                using var w = new StreamWriter(c, Encoding.UTF8);
                foreach (var f in mine) w.WriteLine(f);
                return null;
            }
            catch { return mine.ToList(); }
        }
        var all = new List<string>(mine);
        while (true)
        {
            using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var wait = s.WaitForConnectionAsync();
            if (!wait.Wait(700)) break;
            using var r = new StreamReader(s, Encoding.UTF8);
            string? line;
            while ((line = r.ReadLine()) != null) if (line.Length > 0) all.Add(line);
        }
        mtx.ReleaseMutex();
        return all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length >= 2 && args[0] == "/x")
        {
            int code = 0;
            foreach (var a in args.Skip(1))
            {
                var job = new JobForm(Path.GetFileName(a) + " 푸는 중", (p, ct, ask) =>
                {
                    string? pw = null;
                    while (true)
                    {
                        try { return Engine.Extract(a, pw, null, p, ct); }
                        catch (NeedPasswordException) { pw = ask(); if (pw == null) throw new OperationCanceledException(); }
                    }
                });
                Application.Run(job);
                code = Math.Max(code, job.ExitCode);
            }
            return code;
        }

        if (args.Length >= 2 && args[0] == "/a")
        {
            var items = Collect(args.Skip(1).ToArray());
            if (items == null) return 0;
            var job = new JobForm(Path.GetFileName(Engine.ZipNameFor(items)) + " 만드는 중", (p, ct, _) => Engine.CompressZip(items, null, p, ct));
            Application.Run(job);
            return job.ExitCode;
        }

        Application.Run(new MainForm(args.FirstOrDefault()));
        return 0;
    }
}
