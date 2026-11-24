using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Lsp;
using Xunit;

namespace BeeCoding.Tests;

public class LspAdminSettingsTests
{
    [Fact]
    public void Admin_limits_override_appsettings_and_clearing_them_goes_back()
    {
        var o = new LspOptions { MaxConcurrent = 3, IdleTimeoutSeconds = 90, MemoryLimitMb = 1200, SharedMaxSessions = 40, SharedMaxProcesses = 1 };
        var rc = new PlatformRuntimeConfig();

        rc.ApplyLspLimits(o, new PlatformRuntimeSettings { LspMaxConcurrent = 10, LspIdleTimeoutSeconds = 300, LspMemoryLimitMb = 0, LspSharedMaxSessions = 80, LspSharedMaxProcesses = 2 });
        Assert.Equal((10, 300, 0, 80, 2), (o.MaxConcurrent, o.IdleTimeoutSeconds, o.MemoryLimitMb, o.SharedMaxSessions, o.SharedMaxProcesses));

        rc.ApplyLspLimits(o, new PlatformRuntimeSettings());   // nothing saved: what appsettings.json said
        Assert.Equal((3, 90, 1200, 40, 1), (o.MaxConcurrent, o.IdleTimeoutSeconds, o.MemoryLimitMb, o.SharedMaxSessions, o.SharedMaxProcesses));
    }
}
