#!/bin/bash
set -e 

if [[ -f ".env" ]]; then

  export "$(xargs < .env)"
  nupkgDir="./src/nupkg"
  
  find $nupkgDir -type f -name "*.nupkg" -delete

  dotnet pack --configuration Release

  files=("$nupkgDir/*.nupkg")
  nupkgFile="${files[0]}"

  dotnet nuget push "$nupkgFile" --source https://api.nuget.org/v3/index.json --api-key "$API_KEY"

else
  # .env file template
  # API_KEY=your_api_key_for_nuget
  echo ".env file is not found"
fi