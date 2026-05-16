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

        DepotNodeMapping? existing = await db.DepotNodeMappings
            .AsNoTracking()
            .Where(m => m.DepotKey == _options.DepotKey && m.GraphSignature == graphSignature)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            bool nodeExists = await db.GraphNodes
                .AsNoTracking()
                .AnyAsync(n => n.Id == existing.NodeId, ct);

            if (nodeExists)
            {
                return existing.NodeId;
            }
        }

        using IDisposable? writeLock = graphService.TryAcquireWriteLock();
        if (writeLock is null)
        {
            throw new InvalidOperationException("Failed to acquire graph lock for depot initialization.");
        }

        existing = await db.DepotNodeMappings
            .Where(m => m.DepotKey == _options.DepotKey && m.GraphSignature == graphSignature)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            bool nodeExists = await db.GraphNodes
                .AsNoTracking()
                .AnyAsync(n => n.Id == existing.NodeId, ct);

            if (nodeExists)
            {
                return existing.NodeId;
            }
        }

        int nodeId = await graphService.ApplyNearestEdgeSplitAsync(
            new Position(_options.DepotLatitude, _options.DepotLongitude),
            async newNodeId =>
            {
                db.DepotNodeMappings.Add(new DepotNodeMapping
                {
                    DepotKey = _options.DepotKey,
                    GraphSignature = graphSignature,
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
            "Depot node {DepotNodeId} created for key {DepotKey} and graph signature {GraphSignature}",
            nodeId,
            _options.DepotKey,
            graphSignature);

        return nodeId;
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
