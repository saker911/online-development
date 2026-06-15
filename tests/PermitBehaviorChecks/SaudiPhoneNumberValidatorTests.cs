using Xunit;

namespace PermitBehaviorChecks;

public sealed class SaudiPhoneNumberValidatorTests
{
    [Theory]
    [InlineData("1023456789")]
    [InlineData("2023132222")]
    public void IdentityAcceptsNationalIdAndIqamaPrefixes(string identityNumber)
    {
        Assert.True(SaudiNationalIdOrIqamaValidator.IsValid(identityNumber));
    }

    [Theory]
    [InlineData("3024722918")]
    [InlineData("0024722918")]
    [InlineData("12345")]
    [InlineData("102472291A")]
    [InlineData("10234567890")]
    public void IdentityRejectsInvalidLengthPrefixOrCharacters(string identityNumber)
    {
        Assert.False(SaudiNationalIdOrIqamaValidator.IsValid(identityNumber));
    }

    [Theory]
    [InlineData("0501234567")]
    [InlineData("0559876543")]
    public void MobileAcceptsOnly05Prefix(string phoneNumber)
    {
        Assert.True(SaudiMobileNumberValidator.IsValidRequired(phoneNumber));
    }

    [Theory]
    [InlineData("0101234567")]
    [InlineData("5001234567")]
    [InlineData("051234567")]
    [InlineData("05012345678")]
    [InlineData("05ABC34567")]
    public void MobileRejectsInvalidLengthPrefixOrCharacters(string phoneNumber)
    {
        Assert.False(SaudiMobileNumberValidator.IsValidRequired(phoneNumber));
    }

    [Fact]
    public void LandlineAcceptsOnly01Prefix()
    {
        Assert.True(SaudiLandlineNumberValidator.IsValidRequired("0112345678"));
        Assert.False(SaudiLandlineNumberValidator.IsValidRequired("0501234567"));
    }

    [Theory]
    [InlineData("0501234567")]
    [InlineData("0112345678")]
    public void ContactAcceptsMobileOrLandlinePrefixes(string contactNumber)
    {
        Assert.True(SaudiContactNumberValidator.IsValidRequired(contactNumber));
    }

    [Theory]
    [InlineData("0212345678")]
    [InlineData("05012345678")]
    [InlineData("050123456a")]
    public void ContactRejectsInvalidLengthPrefixOrCharacters(string contactNumber)
    {
        Assert.False(SaudiContactNumberValidator.IsValidRequired(contactNumber));
    }
}
