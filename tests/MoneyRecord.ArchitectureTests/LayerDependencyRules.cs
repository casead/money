using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace MoneyRecord.ArchitectureTests;

/// <summary>
/// Clean Architecture dependency rule enforcement (ARCH-006 §5, BLUE-010 M1).
/// TC-000b: illegal layer references must fail the build gate.
/// </summary>
public class LayerDependencyRules
{
    private const string DomainNamespace = "MoneyRecord.Domain";
    private const string ApplicationNamespace = "MoneyRecord.Application";
    private const string InfrastructureNamespace = "MoneyRecord.Infrastructure";
    private const string ApiNamespace = "MoneyRecord.API";

    [Fact]
    public void Domain_Should_Not_Depend_On_Any_Other_Layer()
    {
        var result = Types.InAssembly(typeof(MoneyRecord.Domain.Common.Money).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain violated: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(MoneyRecord.Application.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Application violated: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(typeof(MoneyRecord.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Infrastructure violated: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }
}
