namespace MoneyRecord.Domain.Entities;

/// <summary>DBD T09 lookup row (seed 1=CashIn, 2=CashOut).</summary>
public class TransactionTypeSeed
{
    public byte Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    public TransactionTypeSeed(byte id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    private TransactionTypeSeed() { } // EF Core
}

/// <summary>DBD T10 lookup row (1=Pending, 2=Completed, 3=Cancelled, 4=Reversed).</summary>
public class TransactionStatusSeed
{
    public byte Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    public TransactionStatusSeed(byte id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    private TransactionStatusSeed() { } // EF Core
}
