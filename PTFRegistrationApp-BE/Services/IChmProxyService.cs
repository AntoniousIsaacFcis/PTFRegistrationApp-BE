using System.Threading;
using System.Threading.Tasks;

namespace PTFRegistrationApp_BE.Services;

public interface IChmProxyService
{
    Task<ProxyResponse> SendAsync(ProxyRequest request, CancellationToken cancellationToken);
}
