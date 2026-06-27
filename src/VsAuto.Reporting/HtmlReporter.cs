using System.Net;
using System.Text;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;

namespace VsAuto.Reporting;

/// <summary>Human-readable HTML report: per-step status, durations, evidence, AI analysis.</summary>
public sealed class HtmlReporter : IReporter
{
    public async Task<string> WriteAsync(CaseResult result, string outDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, $"{result.CaseId}.report.html");
        await File.WriteAllTextAsync(path, Render(result), ct);
        return path;
    }

    private static string Render(CaseResult r)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
        <!doctype html><html><head><meta charset="utf-8">
        <title>{{H(r.CaseId)}} report</title>
        <style>
          body{font-family:system-ui,Segoe UI,Arial,sans-serif;margin:2rem;color:#1b1b1f}
          h1{margin:0 0 .25rem}
          .meta{color:#555;font-size:.9rem;margin-bottom:1rem}
          .badge{display:inline-block;padding:.15rem .5rem;border-radius:.4rem;font-weight:600;color:#fff}
          .Passed{background:#1a7f37}.Failed{background:#c01c28}.Skipped{background:#777}
          table{border-collapse:collapse;width:100%;margin-top:1rem}
          th,td{border:1px solid #ddd;padding:.5rem .6rem;text-align:left;vertical-align:top;font-size:.9rem}
          th{background:#f3f3f5}
          .assert{font-size:.82rem;color:#444;margin:.1rem 0}
          .fail{color:#c01c28}.ok{color:#1a7f37}
          .ai{background:#fff8e6;border-left:3px solid #e3a008;padding:.5rem;margin-top:1rem;white-space:pre-wrap}
          code{background:#f3f3f5;padding:.05rem .3rem;border-radius:.2rem}
        </style></head><body>
        """);

        sb.Append($"<h1>{H(r.CaseId)} — {H(r.Title)} <span class=\"badge {r.Status}\">{r.Status}</span></h1>");
        sb.Append($"<div class=\"meta\">Priority {H(r.Priority)} · {r.Duration.TotalSeconds:F1}s · " +
                  $"{H(r.Environment.Os)} ({H(r.Environment.Arch)}) · driver <code>{H(r.Environment.Driver)}</code>" +
                  $"{(r.Environment.VsVersion is { } v ? $" · VS {H(v)}" : "")} · {H(r.StartedAt.ToString("u"))}</div>");

        sb.Append("<table><thead><tr><th>#</th><th>Step</th><th>Status</th><th>Dur</th>" +
                  "<th>Assertions</th><th>Evidence</th></tr></thead><tbody>");

        var i = 1;
        foreach (var s in r.Steps)
        {
            sb.Append($"<tr><td>{i++}</td><td><strong>{H(s.Name)}</strong><br><code>{H(s.Action)}</code>" +
                      $"{(s.Classification != FailureClass.None ? $"<br><span class=\"fail\">{s.Classification}</span>" : "")}</td>");
            sb.Append($"<td><span class=\"badge {s.Status}\">{s.Status}</span>" +
                      $"{(s.Attempts > 1 ? $"<br><small>{s.Attempts} attempts</small>" : "")}</td>");
            sb.Append($"<td>{s.Duration.TotalSeconds:F1}s</td>");

            sb.Append("<td>");
            foreach (var a in s.Assertions)
            {
                var cls = a.Passed ? "ok" : "fail";
                var adv = a.Advisory ? " <em>(advisory)</em>" : "";
                sb.Append($"<div class=\"assert {cls}\">{(a.Passed ? "✓" : "✗")} <code>{H(a.Type)}</code>{adv} — {H(a.Detail ?? "")}</div>");
            }
            if (s.Error is { } err)
                sb.Append($"<div class=\"assert fail\">error: {H(err)}</div>");
            sb.Append("</td>");

            sb.Append("<td>");
            foreach (var e in s.Evidence)
                sb.Append($"<div><a href=\"{H(ToUri(e))}\">{H(Path.GetFileName(e))}</a></div>");
            sb.Append("</td></tr>");

            if (s.AiAnalysis is { } ai)
                sb.Append($"<tr><td></td><td colspan=\"5\"><div class=\"ai\">🤖 {H(ai)}</div></td></tr>");
        }

        sb.Append("</tbody></table>");

        if (r.SuggestedBug is { } bug)
            sb.Append($"<div class=\"ai\"><strong>Suggested bug summary</strong>\n{H(bug)}</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string H(string s) => WebUtility.HtmlEncode(s);
    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
}
