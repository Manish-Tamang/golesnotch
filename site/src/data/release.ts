// The version the site advertises. Keep in step with <Version> in Notch.csproj and CHANGELOG.md.
export const version = "0.5.0"
export const repo = "https://github.com/Manish-Tamang/golesnotch"

export const downloads = {
  portable: `${repo}/releases/download/v${version}/Notch-${version}-win-x64-portable.zip`,
  runtime: `${repo}/releases/download/v${version}/Notch-${version}-win-x64-requires-dotnet10.zip`,
  latest: `${repo}/releases/latest`,
  all: `${repo}/releases`,
}
