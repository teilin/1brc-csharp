## 1BRC C#

This repo contains my C# solution to 1BRC and the iterations.

## Run

Publish the app in release configuration `dotnet publish -c Release`, then run the application.

## Results

| # | Run time | Run time (ms) | Machine | Comment |
| :--: | :--: | :--: | :-- | :-- |
| #1 | 00:02:36.92 | 156922 | [1] | First naiv attempt in C#. |
| #2 | 00:02:20.86 | 140869 | [1] | Changed from reading line by line to string to read each byte. |
| #3 | 00:02:19.51 | 139517 | [1] | Dealing with byte array directly for the name |
| #4 | 00:01:38.41 | 98415 | [1] | Tricking with the value. Since all measurements only has 1 decimal, I can handle it as int and not decimal or float. |
| #5 | 00:01:38.48 | 98484 | [1] | Setting the project to be AoT (ahead of time) compiled. Had a negativ impact. |
| #6 | 00:01:40.21 | 100212 | [1] | Added a simple list with the three first bytes in the name to be able to sort it faster and then just lookup in the dictionary. |
| #7 | 00:00:20.52 | 20523 | [1] | Introduced paralell processing and SIMD friendly parsing. |
| #8 | 00:00:00.02 | 23 | [1] | Introducted un-safe code and more SIMD parsing. |

### Machines

* [1] : MacBook Pro, Apple M1 Pro, 16 GB