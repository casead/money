using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Domain.Entities;

namespace MoneyRecord.Application.Notifications.Services;

/// <summary>
/// Fires the "ငွေရှိလာပြီ!" reminder when a wallet balance increase makes the shop's
/// pending ပေးရန်ရှိ (payable) credits fully settleable via e-wallet.
/// Only payable credits settle from wallet float — ရရန်ရှိ settles via cash, so a
/// wallet increase is irrelevant for those. Deduped: an unread identical reminder
/// is not re-created. Best-effort: never throws into the caller's transaction.
/// </summary>
public static class CreditReminderNotifier
{
    public static async Task TryNotifyAsync(
        IMoneyRecordDbContext db,
        INotificationService notificationService,
        long shopId,
        long walletAccountId,
        long walletBalanceAfter,
        CancellationToken ct)
    {
        try
        {
            var pendingCredits = await db.Transactions
                .Where(t => t.ShopId == shopId
                    && t.IsCredit
                    && t.Type == TransactionType.CashIn
                    && t.CreditPaymentMethod == "cash" // ပေးရန်ရှိ → settles via E-Wallet
                    && (t.CreditStatus == CreditStatus.Pending
                        || t.CreditStatus == CreditStatus.Confirmed)
                    && t.WalletAccountId == walletAccountId)
                .ToListAsync(ct);

            if (pendingCredits.Count == 0)
                return;

            var totalAmount = pendingCredits.Sum(t => t.Amount);
            // Sufficiency: only claim "can settle" when the float covers everything pending.
            if (walletBalanceAfter < totalAmount)
                return;

            var pendingCount = pendingCredits.Count;
            var message =
                $"ပေးရန်ရှိ customer {pendingCount} ဦးအတွက် အကြွေး {totalAmount:N0} Ks ကို e-wallet ဖြင့် ရှင်းနိုင်ပါပြီ";

            var hasUnread = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.ShopId == shopId
                    && n.Type == NotificationType.CreditReminder
                    && !n.IsRead
                    && n.Message == message, ct);
            if (hasUnread)
                return;

            var data = JsonSerializer.Serialize(new
            {
                screen = "credit",
                pendingCount,
                totalAmount
            });

            await notificationService.NotifyAsync(
                shopId, null,
                NotificationType.CreditReminder,
                "ငွေရှိလာပြီ!",
                message,
                data,
                ct);
        }
        catch { /* best-effort */ }
    }
}
