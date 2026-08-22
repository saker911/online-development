using System.Text.Json;
using Xunit;

namespace PermitBehaviorChecks;

[Trait("Area", TestAreas.Security)]
public sealed class PrivacyMinimizationTests
{
    [Theory]
    [InlineData("owner.main")]
    [InlineData("employee-01")]
    [InlineData("1024722918")]
    public void AccountUsernameSupportsOpaqueAndLegacyIdentifiers(string username)
    {
        Assert.True(AccountUsernameValidator.IsValid(username));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("has spaces")]
    [InlineData("user@example.com")]
    [InlineData("/unsafe")]
    public void AccountUsernameRejectsUnsafeIdentifiers(string username)
    {
        Assert.False(AccountUsernameValidator.IsValid(username));
    }

    [Fact]
    public void InternalReferencesAreRandomAndCannotBeMistakenForNationalIdentity()
    {
        var first = PersonalDataSanitizer.CreateInternalReference();
        var second = PersonalDataSanitizer.CreateInternalReference();

        Assert.StartsWith("REF-", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.False(SaudiNationalIdOrIqamaValidator.IsValid(first));
    }

    [Fact]
    public void AuditJsonRedactsIdentityAndContactFields()
    {
        var json = JsonSerializer.Serialize(
            new
            {
                NationalId = "1024722918",
                PhoneNumber = "0501234567",
                Email = "person@example.test",
                Status = "Approved",
            }
        );

        var sanitized = PersonalDataSanitizer.SanitizeAuditJson(json);

        Assert.DoesNotContain("1024722918", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("0501234567", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.test", sanitized, StringComparison.Ordinal);
        Assert.Contains("Approved", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditTextRedactsCommonIdentityPatterns()
    {
        var sanitized = PersonalDataSanitizer.SanitizeAuditText(
            "الهوية 1024722918 والجوال 0501234567 والبريد person@example.test"
        );

        Assert.DoesNotContain("1024722918", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("0501234567", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.test", sanitized, StringComparison.Ordinal);
    }
}
