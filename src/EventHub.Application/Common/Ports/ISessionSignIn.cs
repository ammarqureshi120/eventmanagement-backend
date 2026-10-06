namespace EventHub.Application.Common.Ports;

/// <summary>
/// Issues and clears the session cookie (AD-16). The Api adapter wraps <c>HttpContext.SignInAsync</c> /
/// <c>SignOutAsync</c>, so Application stays ASP.NET-free (AD-1). Both only stage response headers: nothing reaches
/// the client before the pipeline (and its transaction) has finished, and a failed command clears them again.
/// </summary>
public interface ISessionSignIn
{
    /// <summary>Stages the <c>.EventHub.Session</c> cookie for <paramref name="account"/> (claims <c>sub</c>, <c>role</c>, <c>iat</c>).</summary>
    Task SignInAsync(SessionAccount account, CancellationToken cancellationToken = default);

    /// <summary>Stages the deletion of the session cookie; it is sent even when the request ends in an error.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
