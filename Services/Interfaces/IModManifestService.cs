using System.Collections.Generic;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IModManifestService
{
    IReadOnlyList<ModManifestEntry> BuildModManifest(string gameDir);
    string ComputeManifestHash(IReadOnlyList<ModManifestEntry> manifest);
    int FixMismatches(string gameDir, IEnumerable<ModMismatchItem> mismatches);
}
