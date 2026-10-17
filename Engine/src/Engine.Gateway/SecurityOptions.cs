namespace Engine.Gateway;

public sealed class SecurityOptions
{
    /// <summary>
    /// Websites allowed to open a session or call the API from a browser, e.g. "https://stepwise.example".
    /// Browsers attach an Origin header to WebSocket handshakes and cross-site fetches; a request with an
    /// Origin outside this list is refused. Requests without an Origin (curl, tests, other servers) are not
    /// browsers and are not checked. EMPTY MEANS ANY WEBSITE MAY CONNECT: fine for local development, set it
    /// before exposing the server (environment variable Security__AllowedOrigins__0, __1, ...).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
