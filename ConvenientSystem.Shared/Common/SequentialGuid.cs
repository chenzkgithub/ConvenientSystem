using System.Security.Cryptography;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 顺序 GUID 生成器：生成的 GUID 随时间单调递增，适合作为 SQL Server 聚集索引主键，
    /// 避免随机 GUID 造成的页分裂与索引碎片。
    /// 原理：前 10 字节为随机数，末尾 6 字节放入时间戳——SQL Server 对 UNIQUEIDENTIFIER
    /// 的比较从第 10 字节开始（字节 10~15 权重最高），时间戳放在末尾 6 字节即可保证插入顺序递增。
    /// </summary>
    public static class SequentialGuid
    {
        /// <summary>生成一个按时间递增的 GUID（同一时刻内由随机字节兜底唯一性）。</summary>
        public static Guid NewId()
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);

            // UTC Ticks 低 48 位写入字节 10~15（SQL Server 排序权重最高的位置）
            var ts = DateTime.UtcNow.Ticks;
            bytes[10] = (byte)(ts >> 40);
            bytes[11] = (byte)(ts >> 32);
            bytes[12] = (byte)(ts >> 24);
            bytes[13] = (byte)(ts >> 16);
            bytes[14] = (byte)(ts >> 8);
            bytes[15] = (byte)ts;

            return new Guid(bytes);
        }
    }
}
