# ArdZip

광고 없는 무료 압축 프로그램 (Windows 10·11 64비트)

- 풀기: zip · 7z · rar · tar · gz · bz2 · xz · alz · egg (암호 포함)
- 만들기: zip
- 탐색기 오른쪽 클릭: 「압축 풀기」 「zip 으로 압축」
- 옛날 zip 의 한글 파일명(cp949)도 안 깨짐

다운로드: [Releases](https://github.com/arddong/ardzip/releases/latest) · 소개 페이지: https://arddong.github.io/ardzip/

## 빌드

```
dotnet publish src -c Release -o publish
ISCC.exe ArdZip.iss
```

시험: `dotnet run --project test`

## 라이선스

MIT. 쓴 오픈소스는 [THIRD_PARTY_NOTICES](docs/THIRD_PARTY_NOTICES.txt) 참고.
