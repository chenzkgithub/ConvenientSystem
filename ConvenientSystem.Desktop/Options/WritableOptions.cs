using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ConvenientSystem;

/// <summary>支持运行时回写的选项接口。</summary>
public interface IWritableOptions<out T> : IOptions<T> where T : class
{
    /// <summary>更新配置并持久化到 appsettings.json。</summary>
    void Update(Action<T> applyChanges);
}

/// <summary>可写选项实现：读取 IOptions 当前值，修改后写回 JSON 文件。</summary>
internal sealed class WritableOptions<T> : IWritableOptions<T> where T : class
{
    private readonly string _filePath;
    private readonly string _sectionName;
    private T _value;

    public WritableOptions(IConfiguration configuration, string sectionName)
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _sectionName = sectionName;
        _value = configuration.GetSection(sectionName).Get<T>() ?? Activator.CreateInstance<T>();
    }

    public T Value => _value;

    public void Update(Action<T> applyChanges)
    {
        applyChanges(_value);
        Persist();
    }

    private void Persist()
    {
        try
        {
            var json = File.Exists(_filePath)
                ? File.ReadAllText(_filePath)
                : "{}";

            using var doc = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            var updated = false;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals(_sectionName))
                {
                    writer.WritePropertyName(_sectionName);
                    JsonSerializer.Serialize(writer, _value, new JsonSerializerOptions { WriteIndented = false });
                    updated = true;
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            if (!updated)
            {
                writer.WritePropertyName(_sectionName);
                JsonSerializer.Serialize(writer, _value, new JsonSerializerOptions { WriteIndented = false });
            }

            writer.WriteEndObject();
            writer.Flush();

            File.WriteAllBytes(_filePath, stream.ToArray());
        }
        catch
        {
            // 写配置失败不抛出，避免影响主流程
        }
    }
}

/// <summary>可写选项注册扩展。</summary>
internal static class WritableOptionsExtensions
{
    public static IServiceCollection ConfigureWritable<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName) where T : class
    {
        services.Configure<T>(configuration.GetSection(sectionName));
        services.AddSingleton<IWritableOptions<T>>(sp => new WritableOptions<T>(configuration, sectionName));
        return services;
    }
}
