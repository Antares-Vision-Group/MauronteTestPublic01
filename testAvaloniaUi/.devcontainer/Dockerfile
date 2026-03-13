# Base image with .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0

# Install dependencies for Avalonia GUI preview
RUN apt-get update && apt-get install -y \
    libgtk-3-dev \
    libwebkit2gtk-4.0-dev \
    libx11-dev \
    libxcomposite-dev \
    libxcursor-dev \
    libxdamage-dev \
    libxext-dev \
    libxfixes-dev \
    libxi-dev \
    libxrandr-dev \
    libxrender-dev \
    libxtst-dev \
    libnss3 \
    libasound2 \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user for VS Code
RUN useradd -m vscode && chown -R vscode /home/vscode
USER vscode

WORKDIR /workspace
