using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public sealed record ImageUsageUserDto(int UserId, string Email, string DisplayName, int Images, long Bytes, int Unused, long UnusedBytes);

/// <param name="Unused">Pictures no statement names right now; the janitor deletes them a few days after they were first found unused.</param>
public sealed record ImageUsageDto(int Images, long Bytes, int Unused, long UnusedBytes, int Users, int LimitPerUser, int MaxKb, List<ImageUsageUserDto> Top);

/// <summary>Storage taken by statement pictures (kept in the database), in total and per uploader.</summary>
public static class ImageUsage
{
    /// <summary>Usage of the given pictures (all of them for the platform admin, an organization's members' for its admins);
    /// the heaviest <paramref name="top"/> uploaders by size.</summary>
    public static async Task<ImageUsageDto> BuildAsync(AppDbContext db, IQueryable<StatementImage> images, PlatformRuntimeConfig cfg, int top = 50)
    {
        var perOwner = await images.GroupBy(i => i.OwnerId).Select(g => new
        {
            UserId = g.Key,
            Images = g.Count(),
            Bytes = g.Sum(i => (long)i.Data.Length),
            Unused = g.Count(i => i.UnusedSince != null),
            UnusedBytes = g.Where(i => i.UnusedSince != null).Sum(i => (long)i.Data.Length),
        }).ToListAsync();

        var heaviest = perOwner.OrderByDescending(o => o.Bytes).ThenByDescending(o => o.Images).Take(top).ToList();
        var ids = heaviest.Select(o => o.UserId).ToList();
        var names = await db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.DisplayName }).ToDictionaryAsync(u => u.Id);

        return new ImageUsageDto(
            perOwner.Sum(o => o.Images), perOwner.Sum(o => o.Bytes), perOwner.Sum(o => o.Unused), perOwner.Sum(o => o.UnusedBytes),
            perOwner.Count, cfg.ImageMaxPerUser, cfg.ImageMaxKb,
            heaviest.Select(o => new ImageUsageUserDto(o.UserId, names.GetValueOrDefault(o.UserId)?.Email ?? "(deleted user)",
                names.GetValueOrDefault(o.UserId)?.DisplayName ?? "", o.Images, o.Bytes, o.Unused, o.UnusedBytes)).ToList());
    }
}
