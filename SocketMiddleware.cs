using AIoT.Common.Authorization;
using AIoT.Core.Authorization;
using AIoT.Core.Web;
using AIoT.DeviceCenter.API.Handlers.WebSocketWorker;
using AIoT.DeviceCenter.API.WebSocketWorker;
using IdentityModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AIoT.DeviceCenter.API.Common
{
    /// <summary>
    /// 
    /// </summary>
    [Permission(PermissionCode.AIoT_BasicInfo_Template_Synch)]
    public class SocketMiddleware
    {
        private readonly RequestDelegate _next;
        private ConcurrentDictionary<string, Func<HttpContext, string, Task>> PathMap = new ConcurrentDictionary<string, Func<HttpContext, string, Task>>(StringComparer.OrdinalIgnoreCase);
        private readonly IWebVideoSubscribe _webVideoSubscribe;

        //private IRealalarmSubscribe realalarmSubscribe;
        private IServiceProvider _serviceProvider;
        private readonly IEquipmentSubscribe equipmentSubscribe;
        private IPermissionChecker _permissionChecker;
        private readonly ILogger<SocketMiddleware> _logger;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="next"></param>
        /// <param name="realalarmSubscribe"></param>
        /// <param name="permissionChecker"></param>
        /// <param name="serviceProvider"></param>
        /// <param name="templateoperationruleSynchronization"></param>
        /// <param name="equipmentUploadWorker"></param>
        /// <param name="equipmentSubscribe"></param>
        /// <param name="webVideoSubscribe"></param>
        /// <param name="platformEventSubscribe"></param>
        /// <param name="bindingExpression"></param>
        /// <param name="templatestatusAnalyzeSubscribe"></param>
        /// <param name="ruleImportSubscribe"></param>
        public SocketMiddleware(ILogger<SocketMiddleware> logger, RequestDelegate next,
            IRealalarmSubscribe realalarmSubscribe, IPermissionChecker permissionChecker,
            IServiceProvider serviceProvider, ITemplateoperationruleSynchronization templateoperationruleSynchronization, IEquipmentUploadWorker equipmentUploadWorker, IEquipmentSubscribe equipmentSubscribe,
            IWebVideoSubscribe webVideoSubscribe, IPlatformEventSubscribe platformEventSubscribe, IBindingExpression bindingExpression, ITemplatestatusAnalyzeSubscribe templatestatusAnalyzeSubscribe,
            IRuleImportSubscribe ruleImportSubscribe)
        {
            _webVideoSubscribe = webVideoSubscribe;
            _serviceProvider = serviceProvider;
            this.equipmentSubscribe = equipmentSubscribe;
            _logger = logger;
            _next = next;
            PathMap.AddOrUpdate("/ws/device/v1/equipment/upload", (_http, usrid) =>
            {
                return equipmentUploadWorker.ProcessAsync(_http, usrid);
            }, (key, value) => value);

            PathMap.AddOrUpdate("/ws/device/v1/equipment/subscribe", (_http, usrid) =>
            {
                return equipmentSubscribe.DataSubscribe(_http, usrid);
            }, (key, value) => value);

            //PathMap.AddOrUpdate("/equipment/subscribe", EquipmentSubscribe.DataSubscribe, (key, value) => { return EquipmentSubscribe.DataSubscribe; });
            PathMap.AddOrUpdate("/ws/device/v1/template/sync", TemplateSynchronization.Synchronization, (key, value) => { return TemplateSynchronization.Synchronization; });
            PathMap.AddOrUpdate("/ws/device/v1/equipmenttemplate/sync", TemplateSynchronization.Synchronization, (key, value) => { return TemplateSynchronization.Synchronization; });
            PathMap.AddOrUpdate("/ws/device/v1/templateMethod/sync", TemplateMethodSynchronization.Synchronization, (key, value) => { return TemplateMethodSynchronization.Synchronization; });
            PathMap.AddOrUpdate("/ws/device/v1/equipmentTemplateMethod/sync", EquipmentTemplateMethodSynchronization.Synchronization, (key, value) => { return EquipmentTemplateMethodSynchronization.Synchronization; });
            PathMap.AddOrUpdate("/ws/device/v1/realalarm/subscribe", (_http, usrid) =>
            {
                //var realalarmSubscribe = _serviceProvider.GetService<IRealalarmSubscribe>();
                return realalarmSubscribe.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);
            PathMap.AddOrUpdate("/ws/device/v1/templateoperationrule/sync", (_http, usrid) =>
            {
                return templateoperationruleSynchronization.Synchronization(_http, usrid);
            }, (key, value) => value);
            PathMap.AddOrUpdate("/ws/device/v1/equipmentoperationrule/sync", (_http, usrid) =>
            {
                return templateoperationruleSynchronization.Synchronization(_http, usrid);
            }, (key, value) => value);
            PathMap.AddOrUpdate("/ws/device/v1/video/subscribe", (_http, usrid) =>
            {
                return _webVideoSubscribe.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);

            PathMap.AddOrUpdate("/ws/device/v1/platformevent/subscribe", (_http, usrid) =>
            {
                return platformEventSubscribe.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);

            PathMap.AddOrUpdate("/ws/device/v1/BindingExpression/Subscribe", (_http, usrid) =>
            {
                return bindingExpression.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);

            PathMap.AddOrUpdate("/ws/device/v1/templatestatus/analyze", (_http, usrid) =>
            {
                return templatestatusAnalyzeSubscribe.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);

            PathMap.AddOrUpdate("/ws/device/v1/templatestatus/analyzeplus", (_http, usrid) =>
            {
                return templatestatusAnalyzeSubscribe.SubscribePlusAsnyc(_http, usrid);
            }, (key, value) => value);


            PathMap.AddOrUpdate("/ws/device/v1/ruleimport/subscribe", (_http, usrid) =>
            {
                return ruleImportSubscribe.SubscribeAsnyc(_http, usrid);
            }, (key, value) => value);

            _permissionChecker = permissionChecker;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task Invoke(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                goto _nextInvoke;
            }
            WebSocket webSocket = null;
            try
            {
                string url = $"{context.Request.Path}{context.Request.QueryString}";
                string path = context.Request.Path;
                string uesrId = "";
#if DEBUG
                uesrId = "d44754cdc799473baf05c4587c4d026b";
#endif
                string token = $"";
                if (string.IsNullOrWhiteSpace(uesrId))
                {
                    token = $"{context.Request.Query["accesstoken"]}";
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        _logger.LogDebug($"非法请求，缺失请求参数accesstoken({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "InvalidToken", CancellationToken.None);
                        return;
                    }
                    JwtSecurityToken jwtSecurityToken = new JwtSecurityToken(token);
                    if (jwtSecurityToken == null)
                    {
                        _logger.LogWarning($"accesstoken 解析异常({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "InvalidToken", CancellationToken.None);
                        return;
                    }
                    var exp = jwtSecurityToken.Claims.FirstOrDefault(p => p.Type.Equals(JwtClaimTypes.Expiration))?.Value;
                    long int64Exp = 0L;
                    if (!long.TryParse(exp, out int64Exp))
                    {
                        _logger.LogDebug($"accesstoken 解析异常({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "InvalidToken", CancellationToken.None);
                        return;
                    }
                    DateTime expTime = (new DateTime(1970, 1, 1)).AddSeconds(int64Exp);
                    if (DateTime.Now.ToUniversalTime() > expTime)
                    {
                        _logger.LogDebug($"accesstoken 过期({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "TokenExpired", CancellationToken.None);
                        return;
                    }
                    uesrId = jwtSecurityToken.Claims?.FirstOrDefault(u => u.Type == JwtClaimTypes.Subject)?.Value;
                }


                
                Func<HttpContext, string, Task> process;
                if (PathMap.TryGetValue(path, out process))
                {
                    if (path.Equals("/templateMethod/sync")
                        && !await _permissionChecker.IsGrantedAsync(uesrId, PermissionCode.AIoT_BasicInfo_Template_Synch))
                    {
                        _logger.LogDebug($"未授权({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
                        return;
                    }
                    if (path.Equals("/equipmentTemplateMethod/sync") && !await _permissionChecker.IsGrantedAsync(uesrId, PermissionCode.AIoT_DeviceManage_EquipmentModel_Synch))
                    {
                        _logger.LogDebug($"未授权({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
                        return;
                    }
                    if (path.Equals("/equipmentoperationrule/sync", StringComparison.OrdinalIgnoreCase) && !await _permissionChecker.IsGrantedAsync(uesrId, PermissionCode.AIoT_DeviceManage_EquipmentModel_Synch))
                    {
                        _logger.LogDebug($"未授权({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
                        return;
                    }
                    if (path.Equals("/templateoperationrule/sync", StringComparison.OrdinalIgnoreCase) && !await _permissionChecker.IsGrantedAsync(uesrId, PermissionCode.AIoT_BasicInfo_Template_Synch))
                    {
                        _logger.LogDebug($"未授权({url})");
                        webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
                        return;
                    }
                    await process?.Invoke(context, uesrId);
                    return;
                }
                else
                {
                    //_logger.LogWarning($"NotImplemented({url})");
                    //WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    //await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "NotImplemented", CancellationToken.None);
                    //return;
                    goto _nextInvoke;
                }
            }
            catch (OperationCanceledException)
            {
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    try
                    {
                        if (webSocket == null)
                        {
                            webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        }
                        await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "InternalServerError", CancellationToken.None);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, ex);
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    try
                    {
                        if (webSocket == null)
                        {
                            webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        }
                        await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "InternalServerError", CancellationToken.None);
                    }
                    catch { }
                }
                return;
            }
            _nextInvoke:
            await _next.Invoke(context);
        }
    }
}
