using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Documentation
{
    public static class DocWelcomeSite
    {
        public static void Draw(Rect position)
        {
            Rect rectPart = new(position);
            // Thank you
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
            {
                labelStyle.fontSize = 24; // Set the desired font size
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.alignment = TextAnchor.LowerLeft;

                GUIContent content = new("Thank you\nfor downloading CustomInspector!");
                Vector2 size = labelStyle.CalcSize(content);
                rectPart.height = position.height / 2.1f - 90;
                rectPart.height = Mathf.Max(rectPart.height, size.y);
                EditorGUI.LabelField(rectPart, content, labelStyle);
            }
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.richText = true;
            labelStyle.wordWrap = true;
            // usage
            {
                labelStyle.alignment = TextAnchor.MiddleLeft;
                rectPart.y += rectPart.height + EditorGUIUtility.standardVerticalSpacing;
                rectPart.height = (position.height - rectPart.y) / 3;

                float arrowWidth = 25;
                GUIContent usageText = new("<b>Choose an Attribute</b> from the left sidebar to understand how to use it in your code and view its appearance in the inspector.");
                rectPart.height = Mathf.Max(rectPart.height, labelStyle.CalcHeight(usageText, rectPart.width - arrowWidth));
                Rect arrowRect = new(rectPart) { width = arrowWidth };
                EditorGUI.LabelField(arrowRect, EditorGUIUtility.IconContent(StylesConvert.ToInternalIconName(InspectorIcon.Arrow_left)));
                Rect usageRect = new(rectPart)
                {
                    x = rectPart.x + arrowWidth,
                    width = rectPart.width - arrowWidth
                };
                EditorGUI.LabelField(usageRect, usageText, labelStyle);

            }
            // more info
            {
                //first question
                labelStyle.alignment = TextAnchor.MiddleLeft;
                rectPart.y += rectPart.height + EditorGUIUtility.standardVerticalSpacing;
                GUIContent content = new("Want to share your code made with CustomInspector?");
                rectPart.height = labelStyle.CalcHeight(content, rectPart.width);
                EditorGUI.LabelField(rectPart, content, labelStyle);
                rectPart.y += rectPart.height + EditorGUIUtility.standardVerticalSpacing;

                //referring to placeholders
                Rect linkRect = new(rectPart);
                GUIContent startContent = new("Use ");
                Vector2 size = labelStyle.CalcSize(startContent);
                linkRect.x += size.x;
                linkRect.height = size.y + 3; // because the underline needs more space
                linkRect.position -= new Vector2(3f, 3f); // because of margin and underline, this must be slightly shifted
                content = new("Use <color=#FFFFFF00>CI - Placeholders</color> to publish your source code.");
                rectPart.height = labelStyle.CalcHeight(content, rectPart.width);
                EditorGUI.LabelField(rectPart, content, labelStyle);
                GUIContent linkContent = new("CI - Placeholders");
                linkRect.width = labelStyle.CalcSize(linkContent).x;
                if (EditorGUI.LinkButton(linkRect, linkContent))
                {
                    Application.OpenURL("https://assetstore.unity.com/packages/tools/gui/ci-placeholders-296508");
                }
                rectPart.y += rectPart.height;
            }
            //rate
            {
                // EditorPrefs.DeleteKey("CustomInspector/star_rating");
                int rating = EditorPrefs.GetInt("CustomInspector/star_rating", -1);

                //spacing
                rectPart.y += 20;
                //calc positions
                float starSize = 30;
                GUIContent pleaseRateContent = new("We are committed to enhancing CustomInspector, and your thoughts are crucial. Please take a moment to share your rating of our product:");
                float pleaseRateHeight = labelStyle.CalcHeight(pleaseRateContent, rectPart.width);
                GUIContent negativeFeedback = new("Thank you for taking the time to vote!\nIf you have suggestions for improvement, please feel free to share them directly with us via email at:");
                GUIContent positiveFeedback = new("We appreciate your vote!\nHelp us by forwarding your feedback to the Unity Page:");
                GUIContent feedback = rating == 5 ? positiveFeedback : negativeFeedback;
                float feedbackHeight = labelStyle.CalcHeight(feedback, rectPart.width);

                float totalHeight = pleaseRateHeight
                                    + EditorGUIUtility.standardVerticalSpacing
                                    + starSize
                                    + EditorGUIUtility.standardVerticalSpacing
                                    + feedbackHeight
                                    + EditorGUIUtility.standardVerticalSpacing
                                    + GUI.skin.button.CalcSize(new GUIContent("support@mbwiki.de")).y
                                    + EditorGUIUtility.standardVerticalSpacing;

                rectPart.y = Mathf.Max(rectPart.y, (position.height + position.y) - totalHeight); // max_y - totalHeight, but minimum last height, because rather overflow than printing above other text

                //draw request
                rectPart.height = pleaseRateHeight;
                EditorGUI.LabelField(rectPart, pleaseRateContent, labelStyle);
                rectPart.y += rectPart.height + EditorGUIUtility.standardVerticalSpacing;
                //draw stars
                Rect starRect = new(position: rectPart.position, size: Vector2.one * starSize);
                using (new EditorGUI.DisabledScope(rating != -1))
                {
                    for (int star_index = 1; star_index <= 5; star_index++)
                    {
                        if (GUI.Button(starRect, GUIContent.none)) // make clickable
                        {
                            EditorPrefs.SetInt("CustomInspector/star_rating", star_index);
                        }
                        // EditorGUI.DrawRect(starRect, Color.white); // hide button
                        Color color = Color.white;
                        if (rating != -1 && rating >= star_index)
                            color = Color.yellow;
                        using (new GUIColorScope(color))
                        {
                            labelStyle.alignment = TextAnchor.MiddleCenter;
                            GUI.Label(starRect, EditorGUIUtility.IconContent(StylesConvert.ToInternalIconName(InspectorIcon.Favorite)), labelStyle);
                            labelStyle.alignment = TextAnchor.MiddleLeft;
                        }
                        starRect.x += starRect.width;
                    }
                }
                //draw feedback
                rectPart.y += starSize + EditorGUIUtility.standardVerticalSpacing;
                rectPart.height = feedbackHeight;
                if (rating > 0) // if voted
                {
                    EditorGUI.LabelField(rectPart, feedback, labelStyle);

                    rectPart.y += rectPart.height + EditorGUIUtility.standardVerticalSpacing;
                    if (rating == 5)
                    {
                        GUIContent buttonLabel = new("Asset Store");
                        rectPart.size = GUI.skin.button.CalcSize(buttonLabel);
                        rectPart.width -= 9;
                        if (EditorGUI.LinkButton(rectPart, "Asset Store"))
                        {
                            Application.OpenURL("https://assetstore.unity.com/packages/tools/utilities/custom-inspector-241058#reviews");
                        }
                    }
                    else
                    {
                        GUIContent buttonLabel = new("support@mbservices.de");
                        rectPart.size = GUI.skin.button.CalcSize(buttonLabel);
                        rectPart.width -= 9;
                        if (EditorGUI.LinkButton(rectPart, buttonLabel))
                        {
                            string email = "support@mbservices.de"; // Replace with the recipient's email address
                            string subject = $"CustomInspector rating {rating}/5";   // Replace with your subject
                            string body = "feature requests or problems ..."; // Replace with your message body

                            Application.OpenURL($"mailto:{email}?subject={UnityEngine.Networking.UnityWebRequest.EscapeURL(subject)}&body={UnityEngine.Networking.UnityWebRequest.EscapeURL(body)}".Replace("+", "%20"));
                        }
                    }
                }
            }
        }
    }
}
