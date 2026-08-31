# Simple runtime image with pre-built API files
# API is built locally (dotnet publish) before docker compose up
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Install native dependencies (cached from previous builds)
RUN apt-get update && apt-get install -y --no-install-recommends libgdiplus curl unzip && rm -rf /var/lib/apt/lists/*

# Download libpdfium.so for PDF rendering
RUN mkdir -p /app \
    && curl -sL -o /tmp/docnet.nupkg https://api.nuget.org/v3-flatcontainer/docnet.core/2.3.1/docnet.core.2.3.1.nupkg \
    && mkdir -p /tmp/docnet && cd /tmp/docnet && unzip -q /tmp/docnet.nupkg \
    && cp /tmp/docnet/runtimes/linux/native/pdfium.so /app/libpdfium.so \
    && chmod 755 /app/libpdfium.so && rm -rf /tmp/docnet /tmp/docnet.nupkg

ENV LD_LIBRARY_PATH=/app
WORKDIR /app

# Web 前端版本包与桌面安装包存储目录（docker-compose 挂载 volume）
RUN mkdir -p /data/web-packages /data/desktop-packages

# Copy pre-built API files
COPY api/ .

EXPOSE 51943
ENTRYPOINT ["dotnet", "ConvenientSystem.dll"]
