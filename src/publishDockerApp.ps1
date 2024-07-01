# Run this with ". .\PublishProject.ps1" in Package Manager Console to publish application

function Test-GitInstalled {
    $git = where.exe git
    return -not [string]::IsNullOrEmpty($git)
}

function Test-DockerInstalled {
    $docker = where.exe docker
    return -not [string]::IsNullOrEmpty($docker)
}

function UpdateAndPublishProject 
{
    param(
        $project
    )

    Write-Host "______________Processing $($project.Name)______________"

    $dte.ExecuteCommand("File.SaveAll")

    # Read the .csproj file
    [xml]$csprojContent = Get-Content $project.FullName -Encoding UTF8

    # Check output type to be library
    $sdkAttribute = $csprojContent.Project.GetAttribute("Sdk")
    if ($sdkAttribute -ne "Microsoft.NET.Sdk.Web") {
        Write-Host "$($project.Name) is not using Microsoft.NET.Sdk.Web.."
        return
    }

    $propertyDefaults = @(
        @{ Name = "ContainerRepository"; DefaultValue = $project.Name.ToLower() },
        @{ Name = "ContainerRegistry"; DefaultValue = "" },
        @{ Name = "ContainerImageTag"; DefaultValue = "0.0.0" },
        @{ Name = "ContainerRuntimeIdentifier"; DefaultValue = "linux-x64" },
        @{ Name = "PublishProfile"; DefaultValue = "DefaultContainer" },
        @{ Name = "PublishReadyToRun"; DefaultValue = "true" },
        @{ Name = "PublishReadyToRunComposite"; DefaultValue = "true" }
    )
    $createdDefaultValue = $false
    foreach ($property in $propertyDefaults) {
        $propertyName = $property.Name
        $defaultValue = $property.DefaultValue

        $propertyElement = $csprojContent.SelectSingleNode("//$propertyName")
        if ($null -eq $propertyElement) {
            Write-Host "Adding $propertyName..."
            $propertyGroup = $csprojContent.SelectSingleNode("//PropertyGroup")
            $propertyElement = $csprojContent.CreateElement($propertyName, $propertyGroup.NamespaceURI)
            $propertyElement.InnerText = $defaultValue
            $propertyGroup.AppendChild($propertyElement)
            $createdDefaultValue = $true
        }
    }

    $csprojContent.Save($project.FullName)

    if($createdDefaultValue){
        Write-Host "Created default values, exiting ..."
        return
    }

    $containerRegistryIdElement = $csprojContent.SelectSingleNode("//ContainerRegistry")
    $containerRepositoryElement = $csprojContent.SelectSingleNode("//ContainerRepository")

    if([string]::IsNullOrEmpty($containerRegistryIdElement.InnerText)){
        Write-Host "ContainerRegistry must be set first ..."
        return
    }

    # Find the ContainerImageTag element
    Write-Host "Handling versioning..."
    $ContainerImageTagElement = $csprojContent.SelectSingleNode("//ContainerImageTag")

    # Extract the current version
    $currentVersion = [Version]::Parse($ContainerImageTagElement.InnerText)

    $newVersion = [Version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1)
    
    Write-Host "Incrementing version from `"$currentVersion`" to `"$newVersion`" ..."

    # Update the VersionPrefix in the .csproj file
    $ContainerImageTagElement.InnerText = $newVersion.ToString()

    # Save the changes to the .csproj file
    Write-Host "Saving project information..."
    $csprojContent.Save($project.FullName)

    # Build the Docker image
    $dockerTag = "$($containerRegistryIdElement.InnerText)/$($containerRepositoryElement.InnerText):$newVersion"
    Write-Host "Building Docker image: $dockerTag"
    docker build -t $dockerTag -f $src/Dockerfile --build-arg Version=$newVersion $project.DirectoryName

    # Push the Docker image to the registry
    Write-Host "Pushing Docker image to registry..."
    docker push $dockerTag

    Write-Host "______________Processed $($project.Name)_______________"
    
    git add -A 
    git commit -m "Publish Docker image $dockerTag"
    git tag -a "$($newVersion)" -m "publishDockerApp.ps1 - $($newVersion)"
    git push
    git push --tags
}

# Check if Git is installed
Write-Host "Checking for GIT..."
if (-not (Test-GitInstalled)) {
    Write-Host "Git is not installed. Please install Git and try again."
    return
}

# Check if Docker is installed
Write-Host "Checking for Docker..."
if (-not (Test-DockerInstalled)) {
    Write-Host "Docker is not installed. Please install Docker and try again."
    return
}

# Check the current Git branch
Write-Host "Checking for main branch..."

$currentBranch = git symbolic-ref --short HEAD

# Exit the script if the current branch is not 'main'
if ($currentBranch -ne 'main') {
    Write-Host "Not on the main branch, exiting."
    return
}

# Get the current project from the Package Manager Console
$currentProject = Get-Project

UpdateAndPublishProject $currentProject
