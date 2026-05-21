using Application.Services;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Database;
using Infrastructure.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

internal sealed class DepotNodeService(
    AppDbContext db,
    IGraphService graphService,
    IOptions<RoutingOptions> options,
    ILogger<DepotNodeService> logger) : IDepotNodeService
{
    private readonly RoutingOptions _options = options.Value;

    public async Task<int> GetOrCreateDepotNodeIdAsync(CancellationToken ct = default)
    {
        string graphSignature = await BuildGraphSignatureAsync(ct);

        DepotNodeMapping? existing = await FindByKeyAndSignatureAsync(_options.DepotKey, graphSignature, ct);
        if (existing is not null)
        {
            return existing.NodeId;
        }

        existing = await FindLatestByKeyAsync(_options.DepotKey, ct);
        if (existing is not null)
        {
            return existing.NodeId;
        }

        using IDisposable? writeLock = graphService.TryAcquireWriteLock();
        if (writeLock is null)
        {
            throw new InvalidOperationException("Failed to acquire graph lock for depot initialization.");
        }

        graphSignature = await BuildGraphSignatureAsync(ct);

        existing = await FindByKeyAndSignatureAsync(_options.DepotKey, graphSignature, ct);
        if (existing is not null)
        {
            return existing.NodeId;
        }

        existing = await FindLatestByKeyAsync(_options.DepotKey, ct);
        if (existing is not null)
        {
            return existing.NodeId;
        }

        int nodeId = await graphService.ApplyNearestEdgeSplitAsync(
            new Position(_options.DepotLatitude, _options.DepotLongitude),
            async newNodeId =>
            {
                string postSplitGraphSignature = await BuildGraphSignatureAsync(ct);

                db.DepotNodeMappings.Add(new DepotNodeMapping
                {
                    DepotKey = _options.DepotKey,
                    GraphSignature = postSplitGraphSignature,
                    Latitude = _options.DepotLatitude,
                    Longitude = _options.DepotLongitude,
                    NodeId = newNodeId,
                    CreatedAt = DateTime.UtcNow
                });

                await db.SaveChangesAsync(ct);
            },
            ct
        );

        logger.LogInformation(
            "Depot node {DepotNodeId} created for key {DepotKey}",
            nodeId,
            _options.DepotKey);

        return nodeId;
    }

    private async Task<DepotNodeMapping?> FindByKeyAndSignatureAsync(
        string depotKey,
        string graphSignature,
        CancellationToken ct)
    {
        DepotNodeMapping? existing = await db.DepotNodeMappings
            .AsNoTracking()
            .Where(m => m.DepotKey == depotKey && m.GraphSignature == graphSignature)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return null;
        }

        bool nodeExists = await db.GraphNodes
            .AsNoTracking()
            .AnyAsync(n => n.Id == existing.NodeId, ct);

        return nodeExists ? existing : null;
    }

    private async Task<DepotNodeMapping?> FindLatestByKeyAsync(string depotKey, CancellationToken ct)
    {
        DepotNodeMapping? existing = await db.DepotNodeMappings
            .AsNoTracking()
            .Where(m => m.DepotKey == depotKey)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return null;
        }

        bool nodeExists = await db.GraphNodes
            .AsNoTracking()
            .AnyAsync(n => n.Id == existing.NodeId, ct);

        if (!nodeExists)
        {
            return null;
        }

        return existing;
    }

    private async Task<string> BuildGraphSignatureAsync(CancellationToken ct)
    {
        int nodeCount = await db.GraphNodes.AsNoTracking().CountAsync(ct);
        int edgeCount = await db.GraphEdges.AsNoTracking().CountAsync(ct);

        int maxNodeId = await db.GraphNodes
            .AsNoTracking()
            .Select(n => (int?)n.Id)
            .MaxAsync(ct) ?? 0;

        int maxEdgeId = await db.GraphEdges
            .AsNoTracking()
            .Select(e => (int?)e.Id)
            .MaxAsync(ct) ?? 0;

        return $"{nodeCount}:{edgeCount}:{maxNodeId}:{maxEdgeId}";
    }
}
