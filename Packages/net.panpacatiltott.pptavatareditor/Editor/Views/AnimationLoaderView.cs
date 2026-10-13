using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PPTAvatarEditor
{
    public class AnimationLoaderView : EditorWindow
    {

        private string filePath;
        private string fileName;

        private List<AnimParam> animParamList;

        private Vector2 scrollPos = Vector2.zero;

        void OnEnable()
        {
            filePath = ScriptableSingleton<SendData>.instance.filePath;

            fileName = Path.GetFileName(filePath);

            var anim = AssetDatabase.LoadAssetAtPath(filePath, typeof(AnimationClip)) as AnimationClip;
            animParamList = new FaceEmotion().GetAnimationParamaters(anim);
        }

        void OnGUI()
        {

            EditorGUILayout.LabelField(LocalizeText.instance.langPair.clipName, fileName);

            EditorGUILayout.LabelField(LocalizeText.instance.langPair.propertiesLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos, GUI.skin.box))
            {
                scrollPos = scroll.scrollPosition;
                for (int i = 0; i < animParamList.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        animParamList[i].IsSelect = EditorGUILayout.ToggleLeft(animParamList[i].BlendShapeName, animParamList[i].IsSelect);
                        EditorGUILayout.LabelField($"{animParamList[i].Value}");
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.cancel,
                    () => {
                        this.Close();
                    });

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.loadProperties,
                    () => {
                        LoadAnimationProperties();
                        this.Close();
                    });
            }
        }

        // Callback用
        public static event System.Action OnLoadedAnimationProperties;

        private void LoadAnimationProperties()
        {
            ScriptableSingleton<SendData>.instance.loadingProperties = animParamList.Where(x => x.IsSelect).ToList();

            OnLoadedAnimationProperties();
        }
    }
}
