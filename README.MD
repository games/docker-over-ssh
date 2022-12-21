### A dotnet tools for pushing docker image over SSH

[![NuGet Status](https://img.shields.io/nuget/v/DockerOverSSH.svg?style=flat)](https://www.nuget.org/packages/DockerOverSSH/)


### 1. Install as dotnet tools
```sh
dotnet tool install DockerOverSSH --version 0.0.7-alpha
```

### 2. Push docker image over SSH
```sh
dotnet docker-over-ssh --ssh-key "/Users/dev/.ssh/id_ed25519" \
        --ssh-host "192.168.10.10" \
        --ssh-user "dev" \
        --local-hostname "kubernetes.docker.internal" \
        --local-port 4040 \
        --registry-host "192.168.10.10" \
        --registry-port 5000 \
        --image "myrepo/db:latest" \
        --image "myrepo/service1:latest" \
        --image "myrepo/api:latest"
```

### Notes
- The local image name format should be `$local-hostname:$local-port/$image-id:$image-tag`
