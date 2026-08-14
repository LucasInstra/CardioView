using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CardioView.Services;

public sealed class MitBihAnnotation
{
    public int Code { get; internal set; }
    public int Sample { get; internal set; }
    public int Num { get; internal set; }
    public int Subtype { get; internal set; }
    public int Channel { get; internal set; }
    public string Aux { get; internal set; } = "";
    public char Symbol => MitBihAnnotations.SymbolFor(Code);
    public string Meaning => MitBihAnnotations.MeaningFor(Code);
    public bool IsBeat
    {
        get
        {
            switch (Code)
            {
                case 1: case 2: case 3: case 4: case 5: case 6: case 7:
                case 8: case 9: case 10: case 11: case 12: case 13:
                case 34: case 35: case 37: case 38: case 41:
                    return true;
                default:
                    return false;
            }
        }
    }
    public bool IsQuality => Code == 14;
}

public static class MitBihAnnotations
{
    private static readonly (int code, char symbol, string meaning)[] Table =
    {
        (1,  'N', "Batimento normal"),
        (2,  'L', "Bloqueio de ramo esquerdo"),
        (3,  'R', "Bloqueio de ramo direito"),
        (4,  'a', "Extrassístole atrial aberrante"),
        (5,  'V', "Contração ventricular prematura (PVC)"),
        (6,  'F', "Fusão de batimento ventricular e normal"),
        (7,  'J', "Extrassístole nodal (juncional) prematura"),
        (8,  'A', "Contração atrial prematura"),
        (9,  'S', "Extrassístole supraventricular prematura"),
        (10, 'E', "Batimento de escape ventricular"),
        (11, 'j', "Batimento de escape nodal (juncional)"),
        (12, '/', "Batimento estimulado (marcapasso)"),
        (13, 'Q', "Batimento não classificável"),
        (14, 'n', "Mudança de qualidade do sinal (ruído)"),
        (15, '?', "Batimento não classificado durante aprendizado"),
        (16, '?', "Artefato isolado tipo QRS"),
        (18, 's', "Mudança de segmento ST"),
        (19, 't', "Mudança de onda T"),
        (20, '?', "Sístole"),
        (21, '?', "Diástole"),
        (22, 'x', "Comentário / nota"),
        (23, 'm', "Anotação de medição"),
        (24, 'p', "Pico da onda P"),
        (25, 'b', "Bloqueio de ramo"),
        (26, '|', "Espícula de marcapasso não conduzida"),
        (27, 't', "Pico da onda T"),
        (28, '+', "Mudança de ritmo"),
        (29, 'u', "Pico da onda U"),
        (30, '~', "Aprendizado"),
        (31, '!', "Onda de flutter ventricular"),
        (32, '[', "Início de flutter/fibrilação ventricular"),
        (33, ']', "Fim de flutter/fibrilação ventricular"),
        (34, 'e', "Batimento de escape atrial"),
        (35, 'n', "Batimento de escape supraventricular"),
        (36, 'L', "Link para dados externos"),
        (37, 'x', "Onda P não conduzida (APB bloqueado)"),
        (38, 'f', "Fusão de batimento estimulado e normal"),
        (39, '(', "Início de forma de onda (junção PQ)"),
        (40, ')', "Fim de forma de onda (ponto J)"),
        (41, '?', "Contração ventricular prematura R-sobre-T"),
    };

    public static char SymbolFor(int code)
    {
        foreach (var (c, symbol, _) in Table)
            if (c == code) return symbol;
        return '?';
    }

    public static string MeaningFor(int code)
    {
        foreach (var (c, _, meaning) in Table)
            if (c == code) return meaning;
        return $"Código {code}";
    }

    public static List<MitBihAnnotation> Load(string atrPath)
    {
        byte[] bytes = File.ReadAllBytes(atrPath);
        var list = new List<MitBihAnnotation>();
        int pos = 0;
        int time = 0;
        int num = 0, subtype = 0, channel = 0;
        int len = bytes.Length;

        while (pos + 1 < len)
        {
            int value = bytes[pos] | (bytes[pos + 1] << 8);
            pos += 2;
            int code = (value >> 10) & 0x3F;
            int interval = value & 0x3FF;
            if (code == 0 && interval == 0) break;

            switch (code)
            {
                case 59: // SKIP — 4-byte PDP-11 long interval
                    if (pos + 3 >= len) return list;
                    int high = bytes[pos] | (bytes[pos + 1] << 8);
                    int low = bytes[pos + 2] | (bytes[pos + 3] << 8);
                    pos += 4;
                    time += (high << 16) | low;
                    break;
                case 60: // NUM
                    num = interval;
                    break;
                case 61: // SUB
                    subtype = interval;
                    break;
                case 62: // CHN
                    channel = interval;
                    break;
                case 63: // AUX — text belongs to the previous annotation
                {
                    int nbytes = interval;
                    int take = nbytes + (nbytes & 1);
                    if (pos + take > len) return list;
                    string aux = Encoding.ASCII.GetString(bytes, pos, nbytes).TrimEnd('\0');
                    pos += take;
                    if (list.Count > 0)
                        list[list.Count - 1].Aux = aux;
                    break;
                }
                default:
                    time += interval;
                    list.Add(new MitBihAnnotation
                    {
                        Code = code,
                        Sample = time,
                        Num = num,
                        Subtype = subtype,
                        Channel = channel,
                    });
                    break;
            }
        }

        return list;
    }
}