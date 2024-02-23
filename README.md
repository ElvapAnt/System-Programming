# System Programming

Web API C# projects created on the course of System Programming, utilising various concurrent programming methods such as threading, tasks, asynchronous execution etc. 

## 📷 Web Server ImgToGif conversion

Napomene za izradu domaćeg zadatka: Web server implementirati kao konzolnu aplikaciju koja loguje sve primljene zahteve i informacije o njihovoj obradi (da li je došlo do greške, da li je zahtev uspešno obrađen i ostale ključe detalje). Web server treba da kešira u memoriji odgovore na primljene zahteve, tako da u slučaju da stigne isti zahtev, prosleđuje se već pripremljeni odgovor. Kao klijentsku aplikaciju možete koristiti Web browser ili možete po potrebi kreirati zasebnu konzolnu aplikaciju. Za realizaciju koristiti funkcije iz biblioteke System.Threading, uključujući dostupne mehanizme za sinhronizaciju i zaključavanje. Dozvoljeno je korišćenje ThreadPool-a.

Kreirati Web server koji vrši konverziju slike u gif format. Za proces konverzije se može koristiti ImageSharp (biblioteku je moguće instalirati korišćenjem NuGet package managera). Gif kreirati na osnovu iste slike promenom boje za različite frejmove (frejmovi gifa su varijacije slike u drugoj boji). Osim pomenute, moguće je koristiti i druge biblioteke. Svi zahtevi serveru se šalju preko browser-a korišćenjem GET metode. U zahtevu se kao parametar navodi naziv fajla, odnosno slike. Server prihvata zahtev, pretražuje root folder za zahtevani fajl i vrši konverziju. Ukoliko traženi fajl ne postoji, vratiti grešku korisniku. Primer poziva serveru: http://localhost:5050/slika.png

## 🚀 Reactive Web Server - GitHub repo analysis

