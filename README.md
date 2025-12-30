## 1BRC C#

This repo contains my C# solution to 1BRC and the iterations.

## Results

| # | Run time | Run time (ms) | Machine | Comment |
| :--: | :--: | :--: | :-- | :-- |
| #1 | 00:02:36.92 | 156922 | [1] | First naiv attempt in C#. |
| #2 | 00:02:20.86 | 140869 | [1] | Changed from reading line by line to string to read each byte. |
| #3 | 00:02:19.51 | 139517 | [1] | Dealing with byte array directly for the name |
| #4 | 00:01:38.41 | 98415 | [1] | Tricking with the value. Since all measurements only has 1 decimal, I can handle it as int and not decimal or float. |

### Machines

* [1] : MacBook Pro, Apple M1 Pro, 16 GB