### A dotnet tools for pushing docker image over SSH


### 1. Install as dotnet tools
```sh
dotnet tool install DockerOverSSH --version 0.0.3-alpha
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
        --image-id "myWebApi" \
        --image-tag "latest"
```
