using System.Net;

namespace Momoka.FnNas;

/// <summary>Messages are locally generated: server error bodies may echo passwords or tokens.</summary>
public sealed class FnNasApiException(string message, int? code = null, HttpStatusCode? statusCode = null) : Exception(message)
{
    public int? Code { get; } = code;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool IsTokenExpired => Code == -2 || StatusCode == HttpStatusCode.Unauthorized;
    public bool IsAuthenticationFailure => Code is -2 or -15 or -16 || StatusCode == HttpStatusCode.Unauthorized;

    internal static FnNasApiException Business(int code) => new(code switch
    {
        -1 => "飞牛影视拒绝了请求参数",
        -2 => "飞牛影视登录已失效，请重新登录",
        -3 or 5000 => "飞牛影视请求签名校验失败，请检查设备时间和服务端版本",
        -5 or -16 => "当前飞牛影视账户没有执行此操作的权限",
        -6 => "飞牛影视上找不到该条目",
        -8 => "飞牛影视请求过于频繁，请稍后重试",
        -9 or -3250 => "飞牛影视中已存在同名条目",
        -10 => "请先在飞牛影视网页端完成服务器初始化",
        -11 => "服务器上的文件不存在",
        -13 or -60 => "此飞牛影视版本不支持该操作",
        -14 or -17 => "飞牛影视正在运行相关任务，请稍后重试",
        -15 => "飞牛影视用户名或密码不正确",
        -19 => "飞牛影视元数据存储位置不可用",
        -3400 => "飞牛影视无法移除该媒体库目录",
        _ => $"飞牛影视操作失败（错误码 {code}）"
    }, code);
}
