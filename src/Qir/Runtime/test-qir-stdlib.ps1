# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Push-Location (Join-Path $PSScriptRoot stdlib)
try {
    $releaseFlag = "$Env:BUILD_CONFIGURATION" -eq "Release" ? @("--release") : @();

    # Enable control flow guard (see https://github.com/microsoft/qsharp-runtime/pull/647)
    # for interoperating Rust and C.
    # NB: CFG is only supported on Windows, but the Rust flag is supported on
    #     all platforms; it's ignored on platforms without CFG functionality.
    $Env:RUSTFLAGS = "-C control-flow-guard";

    # Actually run the test.
    cargo test @releaseFlag
    if ($LASTEXITCODE -ne 0) { throw "Failed cargo test on QIR stdlib." }

    # When building in CI, free disk space by cleaning up.
    # Note that this takes longer, but saves ~1 GB of space.
    if ($IsCI) {
        cargo clean;
    }
}
finally {
    Pop-Location
}
