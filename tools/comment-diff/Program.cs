// Proves a comment-only edit: for every changed .cs file, the token stream (comments and whitespace dropped,
// preprocessor directives kept) must equal the version at the given git ref. Exit 1 on any code change.
// Usage: dotnet run --project tools/comment-diff -- [ref=HEAD]
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string gitRef = args.Length > 0 ? args[0] : "HEAD";
string root = Git("rev-parse --show-toplevel").Trim();
var changed = Git($"diff --name-only {gitRef} -- *.cs").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
int bad = 0, ok = 0;
foreach (var rel in changed)
{
    string path = Path.Combine(root, rel);
    if (!File.Exists(path)) { Console.WriteLine($"FAIL {rel}: deleted"); bad++; continue; }
    string before = Git($"show {gitRef}:{rel}");
    string after = File.ReadAllText(path);
    string a = Tokens(before), b = Tokens(after);
    if (a == b) { ok++; continue; }
    int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
    Console.WriteLine($"FAIL {rel}: code differs near: ...{Snip(a, i)}... vs ...{Snip(b, i)}...");
    bad++;
}
Console.WriteLine($"comment-diff: {changed.Count} changed .cs file(s) vs {gitRef}: {ok} comment-only, {bad} with code changes - {(bad == 0 ? "PASS" : "FAIL")}");
return bad == 0 ? 0 : 1;

static string Tokens(string src)
{
    var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None));
    var sb = new StringBuilder();
    foreach (var t in tree.GetRoot().DescendantTokens(descendIntoTrivia: false))
    {
        foreach (var tr in t.LeadingTrivia)
            if (tr.IsDirective) sb.Append(tr.ToString().Trim()).Append('\n');
        sb.Append(t.Text).Append(' ');
    }
    return sb.ToString();
}
static string Snip(string s, int i) => s.Substring(Math.Max(0, i - 40), Math.Min(80, s.Length - Math.Max(0, i - 40))).Replace("\n", " ");
static string Git(string a)
{
    var psi = new ProcessStartInfo("git", a) { RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
    using var p = Process.Start(psi)!;
    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o;
}
