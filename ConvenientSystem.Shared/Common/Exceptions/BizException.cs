namespace ConvenientSystem.Shared.Common.Exceptions
{
    /// <summary>
    /// 业务异常基类：Service 层用它表达可预期的业务失败（校验不通过、记录不存在等），
    /// 由 BizExceptionFilter 统一转换为 { message } 响应体，控制器无需再写错误分支。
    /// </summary>
    public class BizException : Exception
    {
        /// <summary>响应的 HTTP 状态码</summary>
        public int StatusCode { get; }

        /// <summary>
        /// 与 message 平级合并进响应体的附加字段（如缺驱动提示的 hint / downloadUrl）。
        /// 键名按前端读取的原样书写（不参与 camelCase 转换）。
        /// </summary>
        public IReadOnlyDictionary<string, object?>? Extras { get; init; }

        public BizException(string message, int statusCode = StatusCodes.Status400BadRequest)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>参数校验失败或业务规则不满足 → 400</summary>
    public sealed class BadRequestException : BizException
    {
        public BadRequestException(string message)
            : base(message, StatusCodes.Status400BadRequest) { }
    }

    /// <summary>目标记录不存在 → 404</summary>
    public sealed class NotFoundException : BizException
    {
        public NotFoundException(string message = "记录不存在")
            : base(message, StatusCodes.Status404NotFound) { }
    }

    /// <summary>权限不足 → 403</summary>
    public sealed class ForbiddenException : BizException
    {
        public ForbiddenException(string message = "无权访问")
            : base(message, StatusCodes.Status403Forbidden) { }
    }
}
