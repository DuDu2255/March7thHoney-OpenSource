using Google.Protobuf;
using March7thHoney.Configuration;
using March7thHoney.Database.Account;
using March7thHoney.Proto;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Objects;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace March7thHoney.WebServer.Controllers;

[ApiController]
[EnableCors("AllowAll")]
[Route("/")]
public class DispatchRoutes : ControllerBase
{
    private static ConfigContainer Config => ConfigManager.Config;
    public static Logger Logger = new("DispatchServer");
    private static bool DispatchDebugEnabled => Config.ServerOption.LogOption.EnableGamePacketLog;

    private static void Debug(string message)
    {
        if (DispatchDebugEnabled)
            Logger.Debug(message);
    }

    private static void Debug(string message, Exception e)
    {
        if (DispatchDebugEnabled)
            Logger.Debug(message, e);
    }

    [HttpGet("query_dispatch")]
    [HttpPost("query_dispatch")]
    [HttpHead("query_dispatch")]
    public IActionResult QueryDispatch()
    {
        if (!Config.ServerOption.ServerConfig.RunDispatch)
            return Content(string.Empty, "text/plain");

        var publicBaseUrl = GetPublicBaseUrl();

        try
        {
            var req = HttpContext.Request;
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var remotePort = HttpContext.Connection.RemotePort;
            var ua = req.Headers.UserAgent.ToString();
            var isNewFormat = string.Equals(req.Query["is_new_format"].ToString(), "1", StringComparison.Ordinal);

            Debug($"query_dispatch begin: {req.Method} {req.Scheme}://{req.Host}{req.PathBase}{req.Path}{req.QueryString} from {remoteIp}:{remotePort} ua=\"{ua}\"");
            Debug($"query_dispatch format: is_new_format={isNewFormat}");

            foreach (var (key, value) in req.Headers.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    Debug($"query_dispatch header: {key}=<redacted>");
                    continue;
                }

                Debug($"query_dispatch header: {key}={value.ToString()}");
            }

            foreach (var (key, value) in req.Query.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Debug($"query_dispatch query: {key}={value.ToString()}");
            }

            Debug($"query_dispatch config: RunDispatch={Config.ServerOption.ServerConfig.RunDispatch}, RunGateway={Config.ServerOption.ServerConfig.RunGateway}, Regions={Config.ServerOption.ServerConfig.Regions.Count}");
        }
        catch (Exception e)
        {
            Debug("query_dispatch debug prelude failed", e);
        }

        
        var data = new Dispatch();

        if (Config.ServerOption.ServerConfig.RunGateway)
        {
            data.RegionList.Add(new RegionInfo
            {
                Name = Config.GameServer.GameServerId,
                Title = Config.GameServer.GameServerName,
                DispatchUrl = $"{publicBaseUrl}/query_gateway",
                EnvType = Config.GameServer.EnvType.ToString(),
                DisplayName = Config.GameServer.GameServerName
            });
        }

        foreach (var region in Config.ServerOption.ServerConfig.Regions)
        {
            data.RegionList.Add(new RegionInfo
            {
                Name = region.GameServerId,
                Title = region.GameServerName,
                DisplayName = region.GameServerName,
                EnvType = region.EnvType.ToString(),
                DispatchUrl = region.GateWayAddress
            });
        }

        Logger.Info("Client request: query_dispatch");

        var bytes = data.ToByteArray();
        var b64 = Convert.ToBase64String(bytes);

        Debug($"query_dispatch result: regionCount={data.RegionList.Count}, protoBytes={bytes.Length}, base64Length={b64.Length}");
        foreach (var r in data.RegionList)
        {
            Debug($"query_dispatch region: name={r.Name} env={r.EnvType} display={r.DisplayName} url={r.DispatchUrl}");
        }

        return Content(b64, "text/plain");
    }

    private string GetPublicBaseUrl()
    {
        var req = HttpContext.Request;
        var host = req.Host.Value;
        return $"{req.Scheme}://{host}{req.PathBase}".TrimEnd('/');
    }

    [HttpPost("/account/risky/api/check")]
    public ContentResult RiskyCheck()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"id\":\"none\",\"action\":\"ACTION_NONE\",\"geetest\":null}}",
            ContentType = "application/json"
        };
    }

    

    [HttpPost("/hkrpg_global/mdk/shield/api/login")]
    [HttpPost("/{gameKey}/mdk/shield/api/login")]
    public JsonResult Login([FromBody] LoginReqJson req)
    {
        Debug($"shield login: account={req.account} is_crypto={req.is_crypto}");
        var useRsa = Config.WebSecurity.EnableRsaLoginDecrypt && req.is_crypto;
        var (account, password, decryptFailed) = LoginCryptoHelper.TryDecryptCredentialsStrict(req.account, req.password, useRsa);
        if (decryptFailed)
            return new JsonResult(LoginResJson.Error(-202, "Invalid login data"));
        var identityKeys = LoginIdentityContextFactory.Build(Request, req.device, req.device_id, req.device_fp);
        return new UsernameLoginHandler().HandleShieldLogin(account, password, identityKeys);
    }

    [HttpPost("/hkrpg_global/account/ma-passport/api/appLoginByPassword")]
    [HttpPost("/{gameKey}/account/ma-passport/api/appLoginByPassword")]
    public JsonResult PassportLogin([FromBody] PassportLoginReqJson req)
    {
        Debug($"appLoginByPassword: account={req.account}");
        var (account, password) =
            LoginCryptoHelper.TryDecryptCredentials(req.account, req.password, Config.WebSecurity.EnableRsaLoginDecrypt);
        var identityKeys = LoginIdentityContextFactory.Build(Request, req.device, req.device_id, req.device_fp);
        return new UsernameLoginHandler().HandlePassportLogin(account, password, identityKeys);
    }

    [HttpPost("/hkrpg_global/mdk/shield/api/verify")]
    [HttpPost("/{gameKey}/mdk/shield/api/verify")]
    public JsonResult Verify([FromBody] VerifyReqJson req)
    {
        Debug($"shield verify: uid={req.uid} token_len={(req.token?.Length ?? 0)}");
        var identityKeys = LoginIdentityContextFactory.Build(Request);
        return new TokenLoginHandler().HandleShieldVerify(req.uid!, req.token!, identityKeys);
    }

    [HttpPost("/hkrpg_global/combo/granter/login/v2/login")]
    [HttpPost("/{gameKey}/combo/granter/login/v2/login")]
    public JsonResult LoginV2([FromBody] LoginV2ReqJson req)
    {
        Debug($"combo login v2: app_id={req.app_id} channel_id={req.channel_id} data_len={(req.data?.Length ?? 0)}");
        var identityKeys = LoginIdentityContextFactory.Build(Request, req.device, req.data);
        return new ComboTokenGranterHandler().Handle(req.app_id, req.channel_id, req.data!, req.device!, req.sign!,
            identityKeys);
    }

    [HttpPost("/account/ma-cn-passport/app/loginByPassword")]
    public ContentResult LegacyPassportLogin([FromBody] PassportLoginReqJson req)
    {
        Debug($"ma loginByPassword: account={req.account}");
        var (account, password) =
            LoginCryptoHelper.TryDecryptCredentials(req.account, req.password, Config.WebSecurity.EnableRsaLoginDecrypt);
        var identityKeys = LoginIdentityContextFactory.Build(Request, req.device, req.device_id, req.device_fp);
        return new UsernameLoginHandler().HandleLegacyPassportLogin(account, password, identityKeys);
    }

    [HttpPost("/account/ma-cn-session/app/verify")]
    public ContentResult PassportTokenVerify([FromBody] PassportTokenVerifyReqJson req)
    {
        Debug($"ma verify: mid={req.mid}, token_len={(req.token.token?.Length ?? 0)} refresh={req.refresh}");
        var identityKeys = LoginIdentityContextFactory.Build(Request);
        return new TokenLoginHandler().HandlePassportVerify(req.mid!, req.token.token!, req.refresh, identityKeys);
    }

    [HttpGet("/hkrpg_global/combo/granter/api/getConfig")]
    [HttpGet("/{gameKey}/combo/granter/api/getConfig")]
    public ContentResult GetConfig()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"protocol\":true,\"qr_enabled\":false,\"log_level\":\"INFO\",\"announce_url\":\"\",\"push_alias_type\":0,\"disable_ysdk_guard\":true,\"enable_announce_pic_popup\":false,\"app_name\":\"崩�??RPG\",\"qr_enabled_apps\":{\"bbs\":false,\"cloud\":false},\"qr_app_icons\":{\"app\":\"\",\"bbs\":\"\",\"cloud\":\"\"},\"qr_cloud_display_name\":\"\",\"enable_user_center\":true,\"functional_switch_configs\":{}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/hkrpg_global/combo/red_dot/list")]
    [HttpPost("/hkrpg_global/combo/red_dot/list")]
    [HttpGet("/{gameKey}/combo/red_dot/list")]
    [HttpPost("/{gameKey}/combo/red_dot/list")]
    public ContentResult RedDot()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"infos\":[]}}", ContentType = "application/json"
        };
    }

    [HttpGet("/common/hkrpg_global/announcement/api/getAlertAnn")]
    [HttpGet("/common/{gameKey}/announcement/api/getAlertAnn")]
    public ContentResult AlertAnn()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"alert\":false,\"alert_id\":0,\"remind\":false,\"extra_remind\":false}}",
            ContentType = "application/json"
        };
    }


    [HttpGet("/common/hkrpg_global/announcement/api/getAlertPic")]
    [HttpGet("/common/{gameKey}/announcement/api/getAlertPic")]
    public ContentResult AlertPic()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"total\":0,\"list\":[]}}",
            ContentType = "application/json"
        };
    }


    [HttpGet("/hkrpg_global/mdk/shield/api/loadConfig")]
    [HttpGet("/{gameKey}/mdk/shield/api/loadConfig")]
    public ContentResult LoadConfig()
    {
        var gameTokenExpiresIn = Math.Max(Config.ServerOption.Auth.ComboTokenExpireMinutes, 1) * 60;

        return new ContentResult
        {
            Content = JsonConvert.SerializeObject(new
            {
                retcode = 0,
                message = "OK",
                data = new
                {
                    id = 24,
                    game_key = "hkrpg_global",
                    client = "PC",
                    identity = "I_IDENTITY",
                    guest = false,
                    ignore_versions = "",
                    scene = "S_NORMAL",
                    name = "崩�??RPG",
                    disable_regist = true,
                    enable_email_captcha = false,
                    thirdparty = new[] { "fb", "tw", "gl", "ap" },
                    disable_mmt = false,
                    server_guest = false,
                    thirdparty_ignore = new Dictionary<string, object>(),
                    enable_ps_bind_account = false,
                    thirdparty_login_configs = new
                    {
                        tw = new { token_type = "TK_GAME_TOKEN", game_token_expires_in = gameTokenExpiresIn },
                        ap = new { token_type = "TK_GAME_TOKEN", game_token_expires_in = gameTokenExpiresIn },
                        fb = new { token_type = "TK_GAME_TOKEN", game_token_expires_in = gameTokenExpiresIn },
                        gl = new { token_type = "TK_GAME_TOKEN", game_token_expires_in = gameTokenExpiresIn }
                    },
                    initialize_firebase = false,
                    bbs_auth_login = false,
                    bbs_auth_login_ignore = Array.Empty<object>(),
                    fetch_instance_id = false,
                    enable_flash_login = false
                }
            }),
            ContentType = "application/json"
        };
    }


    

    [HttpPost("/hkrpg_global/combo/granter/api/compareProtocolVersion")]
    [HttpPost("/{gameKey}/combo/granter/api/compareProtocolVersion")]
    public ContentResult CompareProtocolVer()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"modified\":false,\"protocol\":null}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/hkrpg_global/mdk/agreement/api/getAgreementInfos")]
    [HttpGet("/{gameKey}/mdk/agreement/api/getAgreementInfos")]
    public ContentResult GetAgreementInfo()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"marketing_agreements\":[]}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/combo/box/api/config/sdk/combo")]
    public ContentResult Combo()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"vals\":{\"kibana_pc_config\":\"{ \\\"enable\\\": 0, \\\"level\\\": \\\"Info\\\",\\\"modules\\\": [\\\"download\\\"] }\\n\",\"network_report_config\":\"{ \\\"enable\\\": 0, \\\"status_codes\\\": [206], \\\"url_paths\\\": [\\\"dataUpload\\\", \\\"red_dot\\\"] }\\n\",\"list_price_tierv2_enable\":\"false\\n\",\"pay_payco_centered_host\":\"bill.payco.com\",\"telemetry_config\":\"{\\n \\\"dataupload_enable\\\": 0,\\n}\",\"enable_web_dpi\":\"true\"}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/combo/box/api/config/sw/precache")]
    public ContentResult Precache()
    {
        return new ContentResult
        {
            Content = "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"vals\":{\"url\":\"\",\"enable\":\"false\"}}}",
            ContentType = "application/json"
        };
    }

    [HttpGet("/device-fp/api/getFp")]
    public JsonResult GetFp([FromQuery] string device_fp)
    {
        return new FingerprintHandler().GetFp(device_fp);
    }

    [HttpGet("/device-fp/api/getExtList")]
    public ContentResult GetExtList()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"message\":\"OK\",\"data\":{\"code\":200,\"msg\":\"ok\",\"ext_list\":[],\"pkg_list\":[],\"pkg_str\":\"/vK5WTh5SS3SAj8Zm0qPWg==\"}}",
            ContentType = "application/json"
        };
    }

    [HttpPost("/data_abtest_api/config/experiment/list")]
    public ContentResult GetExperimentList()
    {
        return new ContentResult
        {
            Content =
                "{\"retcode\":0,\"success\":true,\"message\":\"\",\"data\":[{\"code\":1000,\"type\":2,\"config_id\":\"14\",\"period_id\":\"6125_197\",\"version\":\"1\",\"configs\":{\"cardType\":\"direct\"}}]}",
            ContentType = "application/json"
        };
    }
}

