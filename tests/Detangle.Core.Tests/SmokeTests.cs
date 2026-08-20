using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Phase 0 placeholder. Phase 1 replaces this file with the resolver golden tests
/// over the thirteen fixture vaults and the torture vault.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void EveryResearchedWikiFormatHasAFlavor()
    {
        // Thirteen surveyed formats plus Generic. If this count changes, plan.md
        // section 3.1 needs to change with it.
        Assert.Equal(14, Enum.GetValues<VaultFlavor>().Length);
    }

    [Fact]
    public void GenericIsTheDefaultFlavor()
    {
        Assert.Equal(VaultFlavor.Generic, default(VaultFlavor));
    }
}
