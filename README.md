# 🎮 Alice in Borderland — Épreuves de Survie

> Jeu vidéo 3D développé sous Unity 6 en C#, 
> inspiré de la série Alice in Borderland (Haro Aso).

---

## 📖 Description

Alice in Borderland — Épreuves de Survie est un jeu vidéo 3D 
dans lequel le joueur est plongé dans une ville abandonnée 
post-apocalyptique. Pour survivre, il doit réussir deux épreuves : 
le pilotage d'un drone et la devinette d'un nombre. 
Chaque victoire le rapproche de sa libération.

---

## 🗂️ Structure du projet

Le jeu se compose de 5 scènes :

| Scène | Description |
|-------|-------------|
| Menu Principal | Écran titre et navigation |
| Ville Abandonnée | Hub d'exploration en vue TPS |
| Épreuve Drone | Pilotage de drone, batterie, obstacles |
| DiamondGame | Déduction logique, feedback chaud/froid |
| Écran de Fin | Débloqué après 2 victoires |

---

## 🎮 Comment jouer

1. Lancer le jeu depuis le **Menu Principal**
2. Explorer la **Ville Abandonnée**
3. Approcher une zone colorée et appuyer sur **E** 
   pour entrer dans une épreuve
4. **Épreuve Drone** : pilotez avec WASD + Souris, 
   atteignez la zone d'extraction avant épuisement 
   de la batterie
5. **Épreuve DiamondGame** : trouvez le nombre 
   entre 1 et 100 en moins de 7 tentatives 
   grâce aux indices chaud/froid
6. Remportez les **deux épreuves** pour débloquer 
   l'écran de fin

---

## 🛠️ Technologies utilisées

| Technologie | Usage |
|-------------|-------|
| Unity 6 LTS | Moteur de jeu |
| C# | Langage de programmation |
| URP | Pipeline de rendu |
| TextMeshPro | Interface utilisateur |
| Input System | Gestion clavier/souris |
| Git / GitHub | Versionnement et collaboration |

---

## 📁 Organisation des branches

Ce projet est organisé en deux branches principales :

| Branche | Contenu |
|---------|---------|
| `main` | Scripts Menu, DiamondGame, Ville Abandonnée, navigation |
| `master` | Scripts Drone, City |

---

## 👥 Équipe

Membre:

- Azeroual Khadija
- Bouhouch Hasna
- Bouincha Ikram
- El Mahfoud Nouhaila
- Sarir Abla

---

## 🏫 Contexte académique

- **École** : ENSA MARRAKECH
- **Module** : Développement de Jeux Vidéo
- **Niveau** : 1ère année cycle ingénieur — Génie Informatique
- **Année universitaire** : 2025-2026
- **Encadrant** : M. ATLAS , M. BEKKARI , M. CHAREF

---

## ⚙️ Installation

1. Cloner le dépôt :
```bash
git clone https://github.com/bouikr/alice-in-the-borderland.git
```
2. Ouvrir avec **Unity 6 LTS**
3. Ouvrir la scène `Menu Principal`
4. Cliquer sur **Play**

---

## 📌 Notes

- Plateforme cible : Windows / Mac / Linux
- Contrôles : Clavier + Souris
- Langue : Français
