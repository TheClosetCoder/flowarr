using Flowarr.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowarr.Infrastructure.Data;

public class FlowarrDbContext : DbContext
{
    public FlowarrDbContext(DbContextOptions<FlowarrDbContext> options)
        : base(options)
    {
    }

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<CleanupCandidate> CleanupCandidates => Set<CleanupCandidate>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RemoteTransfer> RemoteTransfers => Set<RemoteTransfer>();
}
