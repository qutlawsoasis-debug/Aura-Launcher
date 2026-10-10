using System.Collections.Generic;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IReleaseNotesService
{
    IReadOnlyList<ReleaseNoteVersion> GetAllVersions();
    ReleaseNoteVersion GetCurrentVersion();
}
