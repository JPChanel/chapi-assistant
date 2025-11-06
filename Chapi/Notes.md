
## instalar paquete q permite generar el ejecutable
```
dotnet tool install -g vpk
```
# Generar relese version
```
dotnet publish "Chapi\Chapi.csproj" -c Release --self-contained -r win-x64 -o ".\publish-output"
```
## 📝 empaquetar app
```
vpk pack --packId ChapiAssistant --packVersion 1.0.0 --packDir ".\publish-output" --mainExe Chapi.exe -o ".\public"
```
git rebase -i HEAD~50
```
```
d 9h8g7f6 Build: Publica la versión v1.0.2   (d ==> delete)
```
```
Esc :wq
```
```
# 1. Elimina las referencias antiguas (reflog)
git reflog expire --expire=now --all

# 2. Ejecuta el "recolector de basura" (Garbage Collector)
git gc --prune=now --aggressive
```
```
# Sube forzadamente tu nueva historia limpia.
# (--force-with-lease es un poco más seguro que --force)
git push --force-with-lease origin main
```
```