using System.Security.Cryptography;
using System.Text;

namespace Mp3Organizer;

public sealed class CopyPlanStore
{
    public string Save(CopyPlan plan, string reports)
    {
        PathSafetyGuard.Separate(plan.Source, reports);
        if (PathSafetyGuard.Within(reports, plan.Target) || PathSafetyGuard.Within(plan.Target, reports)) throw new IOException("Reports must be outside the target.");
        var folder = Path.Combine(reports, plan.PlanId);
        PathSafetyGuard.Writable(folder, plan.Source);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "copy-plan.json");
        var content = JsonFormat.Serialize(plan);
        new M3u8Writer().WriteNew(path, content, plan.Source);
        new M3u8Writer().WriteNew(path + ".sha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), plan.Source);
        return path;
    }
    public CopyPlan Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(File.ReadAllText(path + ".sha256").Trim(), StringComparison.Ordinal)) throw new IOException("Plan checksum mismatch; create a new plan.");
        var plan = JsonFormat.Read<CopyPlan>(Encoding.UTF8.GetString(bytes));
        if (plan.SchemaVersion != 1) throw new IOException("Unsupported plan version.");
        return plan;
    }
    public string Latest(string reports, string source, string target)
    {
        if (Directory.Exists(reports))
            foreach (var path in Directory.GetFiles(reports, "copy-plan.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var plan = Load(path);
                if (string.Equals(plan.Source, PathSafetyGuard.Canonical(source), StringComparison.OrdinalIgnoreCase) && string.Equals(plan.Target, PathSafetyGuard.Canonical(target), StringComparison.OrdinalIgnoreCase)) return path;
            }
        throw new IOException("No previous plan exists for this source and target. Run plan first.");
    }
}
public static class TargetSnapshot
{
    public static string Hash(string target)
    {
        PathSafetyGuard.NoLinks(target);
        if (!Directory.Exists(target)) return "ABSENT";
        var lines = new List<string>();
        Visit(target);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines.Order(StringComparer.Ordinal)))));
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                PathSafetyGuard.NoLinks(path);
                if (Directory.Exists(path)) { lines.Add("D:" + Path.GetRelativePath(target, path)); Visit(path); }
                else
                {
                    using var stream = new ReadOnlySource().OpenRead(path);
                    lines.Add("F:" + Path.GetRelativePath(target, path) + ":" + Convert.ToHexString(SHA256.HashData(stream)));
                }
            }
        }
    }
}
