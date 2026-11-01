// arddong ArdZip 엔진 자가시험 = 한글 이름·빈 폴더·암호·7z·tar.gz·cp949 zip·자동 풀기 자리를 실제 파일로 확인
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using ArdZip;

namespace ArdZipTest;

static class T
{
    static int _fail;
    const string SevenZip = @"C:\Program Files\7-Zip\7z.exe";

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fail++;
    }

    static string Run7z(string args)
    {
        var psi = new ProcessStartInfo(SevenZip, args) { RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception("7z 실패 " + args + "\n" + o);
        return o;
    }

    static bool SameTree(string a, string b)
    {
        var fa = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(a, f)).OrderBy(x => x).ToList();
        var fb = Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(b, f)).OrderBy(x => x).ToList();
        if (!fa.SequenceEqual(fb)) { Console.WriteLine("  목록 다름\n  " + string.Join("|", fa) + "\n  " + string.Join("|", fb)); return false; }
        return fa.All(r => File.ReadAllBytes(Path.Combine(a, r)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, r))));
    }

    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ardzip_test_" + Environment.ProcessId);
        Directory.CreateDirectory(root);
        try
        {
            // 원본 = 한글 폴더 안에 한글 파일·하위 폴더·빈 폴더
            var src = Path.Combine(root, "행사자료");
            Directory.CreateDirectory(Path.Combine(src, "사진", "1일차"));
            Directory.CreateDirectory(Path.Combine(src, "빈폴더"));
            File.WriteAllText(Path.Combine(src, "견적서 최종.txt"), "견적 12,000원");
            File.WriteAllBytes(Path.Combine(src, "사진", "1일차", "무대.bin"), Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray());

            // 1 zip 만들기 = 이름은 폴더명.zip, 같은 이름 있으면 (2)
            var z1 = Engine.CompressZip(new[] { src }, null, null, CancellationToken.None);
            Check(Path.GetFileName(z1) == "행사자료.zip", "zip 이름 = 폴더명.zip");
            var z2 = Engine.ZipNameFor(new[] { src });
            Check(Path.GetFileName(z2) == "행사자료 (2).zip", "같은 이름이면 (2)");

            // 2 다른 프로그램(7-Zip)이 한글 이름을 그대로 읽나
            var listing = Run7z($"l -ba -sccUTF-8 \"{z1}\"");
            Check(listing.Contains("행사자료\\견적서 최종.txt") && listing.Contains("행사자료\\빈폴더"), "7-Zip 이 한글 이름·빈 폴더를 읽음");

            // 3 다시 풀기 = 최상위가 하나라 그 자리에 풀려야 함 → 원본과 이름이 겹치므로 「행사자료 (2)」류 새 폴더로 감
            var outDir = Path.Combine(root, "풀기1"); Directory.CreateDirectory(outDir);
            var zc = Path.Combine(outDir, "행사자료.zip"); File.Copy(z1, zc);
            var d1 = Engine.Extract(zc, null, null, null, CancellationToken.None);
            Check(d1 == outDir, "최상위 하나면 그 자리에 풂");
            Check(SameTree(src, Path.Combine(outDir, "행사자료")), "푼 내용이 원본과 바이트까지 같음");
            Check(Directory.Exists(Path.Combine(outDir, "행사자료", "빈폴더")), "빈 폴더도 살아남");
            var d2 = Engine.Extract(zc, null, null, null, CancellationToken.None);
            Check(Path.GetFileName(d2) == "행사자료 (2)", "이미 있으면 덮지 않고 새 폴더");

            // 4 여러 개 묶기 = 상위 폴더 이름.zip
            var multi = Engine.CompressZip(new[] { Path.Combine(src, "견적서 최종.txt"), Path.Combine(src, "사진") }, null, null, CancellationToken.None);
            Check(Path.GetFileName(multi) == "행사자료.zip", "여러 개 = 상위 폴더 이름.zip");
            var names = ZipFile.OpenRead(multi).Entries.Select(e => e.FullName).ToList();
            Check(names.Contains("견적서 최종.txt") && names.Contains("사진/1일차/무대.bin"), "여러 개 묶을 때 경로 유지");

            // 5 최상위 여러 개인 압축 = 압축 이름 폴더로 풂
            var d3 = Engine.Extract(multi, null, null, null, CancellationToken.None);
            Check(Path.GetFileName(d3).StartsWith("행사자료") && File.Exists(Path.Combine(d3, "견적서 최종.txt")), "최상위 여럿이면 이름 폴더로 풂");

            // 6 7z (7-Zip 으로 만든 것)
            var s7 = Path.Combine(root, "자료.7z");
            Run7z($"a -bso0 \"{s7}\" \"{src}\"");
            var o7 = Path.Combine(root, "o7"); Directory.CreateDirectory(o7);
            Engine.Extract(s7, null, o7, null, CancellationToken.None);
            Check(SameTree(src, Path.Combine(o7, "행사자료")), "7z 풀기");

            // 7 암호 zip·암호 7z(머리말까지 암호)
            var pz = Path.Combine(root, "암호.zip");
            Run7z($"a -bso0 -tzip -p1234 -mem=AES256 \"{pz}\" \"{src}\"");
            bool asked = false;
            try { Engine.Extract(pz, null, Path.Combine(root, "pz0"), null, CancellationToken.None); } catch (NeedPasswordException) { asked = true; }
            Check(asked, "암호 zip 에 암호 없이 = 암호 묻기");
            bool wrong = false;
            try { Engine.Extract(pz, "9999", Path.Combine(root, "pzw"), null, CancellationToken.None); } catch (NeedPasswordException) { wrong = true; } catch (Exception ex) { Console.WriteLine("  틀린 암호 예외 = " + ex.GetType().Name + " " + ex.Message); }
            Check(wrong, "틀린 암호 = 다시 묻기");
            Engine.Extract(pz, "1234", Path.Combine(root, "pz1"), null, CancellationToken.None);
            Check(SameTree(src, Path.Combine(root, "pz1", "행사자료")), "암호 zip 풀기(AES)");

            var p7 = Path.Combine(root, "암호.7z");
            Run7z($"a -bso0 -p1234 -mhe=on \"{p7}\" \"{src}\"");
            asked = false;
            try { Engine.List(p7, null); } catch (NeedPasswordException) { asked = true; } catch (Exception ex) { Console.WriteLine("  암호7z 예외 = " + ex.GetType().Name + " " + ex.Message); }
            Check(asked, "머리말 암호 7z = 암호 묻기");
            Engine.Extract(p7, "1234", Path.Combine(root, "p71"), null, CancellationToken.None);
            Check(SameTree(src, Path.Combine(root, "p71", "행사자료")), "암호 7z 풀기");

            // 8 옛날식(cp949) 이름 zip
            var cz = Path.Combine(root, "옛날.zip");
            Run7z($"a -bso0 -tzip -mcu=off -mcp=949 \"{cz}\" \"{src}\"");
            var cl = Engine.List(cz, null).Select(i => i.Path).ToList();
            Check(cl.Contains("행사자료/견적서 최종.txt"), "cp949 이름 zip 한글 안 깨짐  " + string.Join("|", cl.Take(3)));

            // 9 tar.gz
            var tar = Path.Combine(root, "자료.tar");
            Run7z($"a -bso0 -ttar \"{tar}\" \"{src}\"");
            var tgz = tar + ".gz";
            Run7z($"a -bso0 -tgzip \"{tgz}\" \"{tar}\"");
            var ot = Path.Combine(root, "ot"); Directory.CreateDirectory(ot);
            try
            {
                Engine.Extract(tgz, null, ot, null, CancellationToken.None);
                Check(SameTree(src, Path.Combine(ot, "행사자료")), "tar.gz 한 번에 풀기  " + string.Join("|", Directory.EnumerateFileSystemEntries(ot).Select(Path.GetFileName)));
            }
            catch (Exception ex) { Check(false, "tar.gz 풀기 예외 " + ex.Message); }

            // 10 egg·alz (EggDotNet 시험 파일이 있으면)
            foreach (var egg in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "samples")).Where(f => f.EndsWith(".egg") || f.EndsWith(".alz")))
            {
                try
                {
                    var items = Engine.List(egg, null);
                    var oe = Path.Combine(root, "egg_" + Path.GetFileName(egg));
                    Engine.Extract(egg, null, oe, null, CancellationToken.None);
                    int n = Directory.EnumerateFiles(oe, "*", SearchOption.AllDirectories).Count();
                    Check(n == items.Count(i => !i.IsDir) && n > 0, $"{Path.GetFileName(egg)} 풀기 = 파일 {n}개");
                }
                catch (NeedPasswordException)
                {
                    // EggDotNet 시험 파일 암호
                    Check(!Directory.Exists(Path.Combine(root, "egg_" + Path.GetFileName(egg))), "암호 묻기 전 만든 폴더는 지움");
                    var oe = Path.Combine(root, "eggpw_" + Path.GetFileName(egg));
                    Engine.Extract(egg, "password12345!", oe, null, CancellationToken.None);
                    var txt = File.ReadAllText(Directory.EnumerateFiles(oe, "*", SearchOption.AllDirectories).First());
                    Check(txt.StartsWith("Lorem ipsum") && txt.Length == 15_238, $"{Path.GetFileName(egg)} 암호 묻고 맞는 암호로 풂");
                }
                catch (Exception ex) { Check(false, $"{Path.GetFileName(egg)} 예외 {ex}"); }
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine(_fail == 0 ? "ALL PASS" : $"FAIL {_fail}");
        return _fail == 0 ? 0 : 1;
    }
}
