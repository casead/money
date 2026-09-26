using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.API.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMoneyRecordDbContext _db;
    private readonly ICurrentUser _currentUser;

    public NotificationsController(IMoneyRecordDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, [FromQuery] bool? isRead = null,
        CancellationToken ct = default)
    {
        var shopId = _currentUser.ShopId;
        if (shopId is null) return Ok(new { data = new object[0], pagination = new { page, pageSize, totalItems = 0, totalPages = 0 } });

        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.ShopId == shopId.Value);

        if (isRead.HasValue)
            query = query.Where(n => n.IsRead == isRead.Value);

        query = query.OrderByDescending(n => n.CreatedAt);

        var totalItems = await query.CountAsync(ct);
        // Materialize first: the Mongo provider would translate Type.ToString()
        // server-side ($toString on the numeric field) and return "3" instead
        // of "CreditReminder".
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        var items = rows
            .Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                title = n.Title,
                message = n.Message,
                data = n.Data,
                isRead = n.IsRead,
                createdAt = n.CreatedAt
            })
            .ToList();

        return Ok(new
        {
            data = items,
            pagination = new { page, pageSize, totalItems, totalPages = (int)Math.Ceiling((double)totalItems / pageSize) }
        });
    }

    [HttpGet("count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var shopId = _currentUser.ShopId;
        if (shopId is null) return Ok(new { data = 0 });

        var count = await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.ShopId == shopId.Value && !n.IsRead, ct);

        return Ok(new { data = count });
    }

    [HttpPut("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var shopId = _currentUser.ShopId;
        if (shopId is null) return NotFound();

        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.ShopId == shopId.Value, ct);

        if (notification is null) return NotFound();

        notification.MarkRead();
        await _db.SaveChangesAsync(ct);

        return Ok(new { data = "ok" });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var shopId = _currentUser.ShopId;
        if (shopId is null) return Ok(new { data = 0 });

        var unread = await _db.Notifications
            .Where(n => n.ShopId == shopId.Value && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread) n.MarkRead();
        await _db.SaveChangesAsync(ct);

        return Ok(new { data = unread.Count });
    }

    [HttpPost("fcm-token")]
    public async Task<IActionResult> RegisterFcmToken([FromBody] FcmTokenRequest body,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? 0;
        var shopId = _currentUser.ShopId ?? 0;

        var existing = await _db.FcmTokens
            .Where(t => t.UserId == userId && t.Token == body.Token)
            .ToListAsync(ct);

        if (existing.Count == 0)
        {
            var fcmToken = FcmToken.Create(userId, shopId, body.Token, DateTime.UtcNow);
            _db.FcmTokens.Add(fcmToken);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { data = "ok" });
    }

    public sealed record FcmTokenRequest(string Token);
}
