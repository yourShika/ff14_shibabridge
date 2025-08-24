# ShibaBridge Server

This is a minimal ASP.NET Core server used by the ShibaBridge plugin. It exposes
REST endpoints for registration, authentication and player pairing and hosts a
SignalR hub for real-time synchronization of Penumbra and Glamourer data.

## Building locally

```bash
cd Server/ShibaBridge.Server
dotnet run
```

## Docker

A Dockerfile is provided for convenience. It publishes the server and runs it
in an ASP.NET runtime container.

```bash
docker build -t shibabridge-server -f Server/Dockerfile .
docker run -p 8080:8080 shibabridge-server
```

The server is **not** production ready. It uses in-memory stores and dummy
authentication tokens but outlines where real implementations would be placed.
