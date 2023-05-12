1) Uvod
- Co je to typova inference
- Co je to roslyn, a jak v ni funguje typova inference
- Omezeni typove inference v Roslynu
2) Related work
- Odkazy na github issues
- Jak to funguje v Rustu
- Hindley-Milner type inference
3) Analyza
- Identifikace problemu
	- Jak jsem vybral skupinu problemu, co budu resit + mozna budouci rozsireni doplnujici tyhle problemy
	- Breaking changes
- Popsat ocekavany vystup -> prototyp + zmena specifikace(viz proposal)
- Kam zapada roslyn method type inference do Hindley-Milnerovi type inference
- Problemy pri navrhu partial type inference
	- underscore
	- Constructors
- Problemy pri navrhu type inference z initializatoru
	- Object initializer
	- Collection initializer
- Problemy pri partial variable declararion
- Problemy pri partial casting
(Pokud se stihne)
- Problemy pri where clauses v inferenci
- Problemy pri target-typed v inferenci
- Problemy pri inferovani return type v lokalnich funkci
- Problemy pri multipli implemented interfaces
4) Prototyp
- Popsani implementace vyse zminenych problemu
5) Psani proposals
- Popsani vytvareni proposalu pro LDM
6) Vysledky z prototypu
7) Shrnuti
