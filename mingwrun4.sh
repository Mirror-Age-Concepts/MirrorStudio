cd '/c/Users/Mirror Age Concepts/source/repos/MirrorStudio'
DLL='MirrorStudio.App/bin/Debug/net9.0/MirrorStudio.App.dll'
timeout 20 dotnet "$DLL" > stackdump.txt 2>&1
echo "EXIT=$?"
echo '=== TOP of stack (innermost frames) ==='
head -n 55 stackdump.txt
