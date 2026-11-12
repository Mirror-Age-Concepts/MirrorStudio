cd '/c/Users/Mirror Age Concepts/source/repos/MirrorStudio'
DLL='MirrorStudio.App/bin/Debug/net9.0/MirrorStudio.App.dll'
timeout 30 dotnet "$DLL" > /tmp/rE.txt 2>&1
rc=$?
echo "EXIT=$rc"
echo '=== TOP of stack (innermost frames) ==='
head -n 60 /tmp/rE.txt
echo '=== all VideoStudioView frames ==='
grep -c 'VideoStudioView' /tmp/rE.txt
