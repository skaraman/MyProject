using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class FontCharacter : MonoBehaviour {
  public SpriteResolver spriteResolver;
  public char character { set; get; } = 'T';
  public string font { set; get; } = "Hand";

  public Dictionary<char, string> cacheBank = new Dictionary<char, string> { // char.ToString() is too expensive, this is faster
    // Lowercase letters a-z
    {'a', "a"}, {'b', "b"}, {'c', "c"}, {'d', "d"}, {'e', "e"}, {'f', "f"}, {'g', "g"}, {'h', "h"},
    {'i', "i"}, {'j', "j"}, {'k', "k"}, {'l', "l"}, {'m', "m"}, {'n', "n"}, {'o', "o"}, {'p', "p"},
    {'q', "q"}, {'r', "r"}, {'s', "s"}, {'t', "t"}, {'u', "u"}, {'v', "v"}, {'w', "w"}, {'x', "x"},
    {'y', "y"}, {'z', "z"},
    
    // Uppercase letters A-Z
    {'A', "A"}, {'B', "B"}, {'C', "C"}, {'D', "D"}, {'E', "E"}, {'F', "F"}, {'G', "G"}, {'H', "H"},
    {'I', "I"}, {'J', "J"}, {'K', "K"}, {'L', "L"}, {'M', "M"}, {'N', "N"}, {'O', "O"}, {'P', "P"},
    {'Q', "Q"}, {'R', "R"}, {'S', "S"}, {'T', "T"}, {'U', "U"}, {'V', "V"}, {'W', "W"}, {'X', "X"},
    {'Y', "Y"}, {'Z', "Z"},
    
    // Numbers 0-9
    {'0', "0"}, {'1', "1"}, {'2', "2"}, {'3', "3"}, {'4', "4"}, {'5', "5"}, {'6', "6"}, {'7', "7"},
    {'8', "8"}, {'9', "9"},
    
    // Common punctuation and symbols
    {' ', " "}, {'!', "!"}, {'"', "\""}, {'#', "#"}, {'$', "$"}, {'%', "%"}, {'&', "&"}, {'\'', "'"},
    {'(', "("}, {')', ")"}, {'*', "*"}, {'+', "+"}, {',', ","}, {'-', "-"}, {'.', "."}, {'/', "/"},
    {':', ":"}, {';', ";"}, {'<', "<"}, {'=', "="}, {'>', ">"}, {'?', "?"}, {'@', "@"}, {'[', "["},
    {'\\', "\\"}, {']', "]"}, {'^', "^"}, {'_', "_"}, {'`', "`"}, {'{', "{"}, {'|', "|"}, {'}', "}"},
    {'~', "~"}
  };
  void Reset() {
    spriteResolver = GetComponent<SpriteResolver>();
  }

  [ForceUpdate]
  public void UpdateSprite() {
    if (spriteResolver) spriteResolver.SetCategoryAndLabel(font, cacheBank[character]);
  }
}

