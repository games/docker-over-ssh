if [ ! -f "$pwd/.env" ]
then

  export $(cat .env | xargs)

  nupkgDir="./src/nupkg"
  rm -rf "$nupkgDir/*.nupkg"

  dotnet pack --configuration Release

  files=($nupkgDir/*.nupkg)
  nupkgFile="${files[0]}"

  dotnet nuget push $nupkgFile --source https://api.nuget.org/v3/index.json --api-key $API_KEY

else
  echo ".env file is not found"
fi