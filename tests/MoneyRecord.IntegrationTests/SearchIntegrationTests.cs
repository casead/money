using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MoneyRecord.Application.Balances.Commands;
using MoneyRecord.Application.Common.Interfaces;
using MoneyRecord.Application.Transactions.Commands;
using MoneyRecord.Application.Transactions.Queries;

namespace MoneyRecord.IntegrationTests;

/// <summary>
/// TXN-004 search behavior: phone-like terms take the indexed path
/// (txnNo / phone-prefix / amount only — name match skipped);
/// non-phone terms keep name Contains.
/// </summary>
[Collection("mongo")]
public class SearchIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbFixture _fx;
    private long _accountId;

    public SearchIntegrationTests(MongoDbFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var acc = await sender.Send(new CreateWalletAccountCommand(
            1, "Wave IT", $"0976{Random.Shared.Next(1000000, 9999999)}", 500_000));
        _accountId = acc.Value!.Id;

        await sender.Send(new AdjustBalanceCommand(
            "cash", null, "INCREASE", 300_000, "opening cash for search tests", null));
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    private CreateCashInCommand In(string name, string phone, long amount) => new()
    {
        IdempotencyKey = Guid.NewGuid(),
        CustomerName = name,
        CustomerPhone = phone,
        WalletAccountId = _accountId,
        Amount = amount,
        FeePaidVia = "cash"
    };

    [Fact]
    public async Task PhoneLikeTerm_MatchesPhoneTxn_SkipsNameOnlyTxn()
    {
        using var scope = _fx.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var phone = $"0997{Random.Shared.Next(1000000, 9999999)}";
        var phoneTxn = await sender.Send(In("Daw Search Phone", phone, 12_345));
        phoneTxn.IsSuccess.Should().BeTrue();

        var digitName = $"Mg 0991{Random.Shared.Next(1000, 9999)} Named";
        var nameTxn = await sender.Send(In(digitName, "09880001112", 23_456));
        nameTxn.IsSuccess.Should().BeTrue();

        // (1) phone-like term finds the phone txn
        var byPhone = await sender.Send(new SearchTransactionsQuery(phone));
        byPhone.IsSuccess.Should().BeTrue();
        byPhone.Value.Should().Contain(r => r.TxnNo == phoneTxn.Value!.TxnNo);

        // (2) phone-like term built from a NAME's digits must NOT return the name-only txn
        var digitsFromName = new string(digitName.Where(char.IsDigit).ToArray());
        digitsFromName.Should().StartWith("0991", "the term must be phone-like to take the indexed path");
        var byDigits = await sender.Send(new SearchTransactionsQuery(digitsFromName));
        byDigits.IsSuccess.Should().BeTrue();
        byDigits.Value.Should().NotContain(r => r.TxnNo == nameTxn.Value!.TxnNo);

        // (3) non-phone term still matches CustomerNameSnapshot.Contains
        var byName = await sender.Send(new SearchTransactionsQuery("Search Phone"));
        byName.IsSuccess.Should().BeTrue();
        byName.Value.Should().Contain(r => r.TxnNo == phoneTxn.Value!.TxnNo);
    }
}
