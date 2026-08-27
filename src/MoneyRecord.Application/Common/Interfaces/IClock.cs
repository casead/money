using MoneyRecord.Domain.Common;

namespace MoneyRecord.Application.Common.Interfaces;

/// <summary>
/// Application-level alias of the domain clock. Kept so existing Application/Infrastructure
/// code compiles unchanged; the canonical interface lives in Domain.
/// </summary>
public interface IClock : Domain.Common.IClock
{
}
