using RingInfo.Core.Auth;

namespace RingInfo.Core.Tests.Auth;

public class PkceTests
{
    [Fact]
    public void Ouraのドキュメント記載の例と同じ_code_challenge_になる()
    {
        // https://cloud.ouraring.com/docs/authentication の例
        var challenge = Pkce.CreateCodeChallenge("QUW2bsoMYITWSSPUgCDEc4CXCBfcVyu8TkujreHe8CXP4K4d4463gRztuxfo96YP");

        Assert.Equal("43Kb1gELSIjkvwzrPtAg0Lz2HqCG3BtS_SUMAQhjv7c", challenge);
    }

    [Fact]
    public void code_verifier_は仕様の長さと文字種を満たす()
    {
        var verifier = Pkce.CreateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.CreateCodeVerifier());
    }

    [Fact]
    public void state_は毎回異なる()
    {
        Assert.NotEqual(Pkce.CreateState(), Pkce.CreateState());
    }
}
