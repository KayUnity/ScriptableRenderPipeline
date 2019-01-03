﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.Graphing.Util;
using UnityEditor.ShaderGraph.Drawing;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEngine.UIElements;

namespace UnityEditor.ShaderGraph
{
    [Title("Custom Code")]
    class CustomCodeNode : AbstractMaterialNode
        , IHasSettings
        , IGeneratesBodyCode
        , IGeneratesFunction
        , IMayRequireNormal
        , IMayRequireTangent
        , IMayRequireBitangent
        , IMayRequireMeshUV
        , IMayRequireScreenPosition
        , IMayRequireViewDirection
        , IMayRequirePosition
        , IMayRequireVertexColor
    {
        [SerializeField]
        SerializableSlot[] m_SerializableInputSlots = { new SerializableSlot(0, "In", SlotType.Input, SlotValueType.Vector1, 0) };

        [SerializeField]
		List<MaterialSlot> m_InputSlots;

        //[DynamicSlotListControl(SlotType.Input)]
		public List<MaterialSlot> inputSlots
		{
			get 
            { 
                if (m_SerializableInputSlots != null)
                {
                    m_InputSlots = new List<MaterialSlot>();
                    for(int i = 0; i < m_SerializableInputSlots.Length; i++)
                        m_InputSlots.Add(m_SerializableInputSlots[i].Deserialize());
                    m_SerializableInputSlots = null;
                }
                return m_InputSlots; 
            }
		}

        [SerializeField]
        SerializableSlot[] m_SerializableOutputSlots = { new SerializableSlot(1, "Out", SlotType.Output, SlotValueType.Vector1, 0) };

        [SerializeField]
		List<MaterialSlot> m_OutputSlots;
        
        //[DynamicSlotListControl(SlotType.Output)]
		public List<MaterialSlot> outputSlots
		{
			get 
            {
                if (m_SerializableOutputSlots != null)
                {
                    m_OutputSlots = new List<MaterialSlot>();
                    for(int i = 0; i < m_SerializableOutputSlots.Length; i++)
                        m_OutputSlots.Add(m_SerializableOutputSlots[i].Deserialize());
                    m_SerializableOutputSlots = null;
                }
                return m_OutputSlots; 
            }
		}

		[SerializeField]
		private string m_Code = "Out = In;";

		[TextFieldControl("", true)]
		public string code
		{
			get { return m_Code; }
			set { m_Code = value; }
		}

        ButtonConfig m_ButtonConfig;

        [ButtonControl]
        public ButtonConfig buttonConfig
        {
            get { return m_ButtonConfig; }
        }

        [SerializeField]
        private bool m_DisplayPreview = true;

        public override bool hasPreview { get { return true; } }
        public override bool activePreview { get { return m_DisplayPreview; } }

        public CustomCodeNode()
        {
            name = "Custom Code";
            UpdateNodeAfterDeserialization();

            m_ButtonConfig = new ButtonConfig()
            {
                text = "Recompile",
                action = () =>
                {
                    Dirty(ModificationScope.Topological); 
                }
            };
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            List<int> validSlots = new List<int>();

            foreach(MaterialSlot slot in inputSlots)
            {
                AddSlot(slot);
                validSlots.Add(slot.id);
            }
                
            foreach(MaterialSlot slot in outputSlots)
            {
                AddSlot(slot);
                validSlots.Add(slot.id);
            }

            RemoveSlotsNameNotMatching(validSlots);
        } 

        public override void OnBeforeSerialize()
        {
            base.OnBeforeSerialize();
            if (m_InputSlots != null)
            {
                m_SerializableInputSlots = new SerializableSlot[m_InputSlots.Count];
                for(int i = 0; i < m_InputSlots.Count; i++)
                    m_SerializableInputSlots[i] = m_InputSlots[i].Serialize();
            }
            if (m_OutputSlots != null)
            {
                m_SerializableOutputSlots = new SerializableSlot[m_OutputSlots.Count];
                for(int i = 0; i < m_OutputSlots.Count; i++)
                    m_SerializableOutputSlots[i] = m_OutputSlots[i].Serialize();
            }
        }

		string GetFunctionName()
        {
            return string.Format("Unity_CustomCode_{0}_{1}", GuidEncoder.Encode(guid), precision);
        }

		public void GenerateNodeCode(ShaderGenerator visitor, GraphContext context, GenerationMode generationMode)
        {
			var arguments = new List<string>();

            for(int i = 0; i < inputSlots.Count; i++)
                arguments.Add(GetSlotValue(inputSlots[i].id, generationMode));

			for(int i = 0; i < outputSlots.Count; i++)
                arguments.Add(GetVariableNameForSlot(outputSlots[i].id));

            if(arguments.Count == 0)
                return;

            for(int i = 0; i < outputSlots.Count; i++)
            {
                visitor.AddShaderChunk(string.Format("{0} {1};", 
                    FindOutputSlot<MaterialSlot>(outputSlots[i].id).concreteValueType.ToString(precision), 
                    GetVariableNameForSlot(outputSlots[i].id)), false);
            }

			visitor.AddShaderChunk(
                string.Format("{0}({1});"
                    , GetFunctionName()
                    , arguments.Aggregate((current, next) => string.Format("{0}, {1}", current, next)))
                , false);
        }

		public void GenerateNodeFunction(FunctionRegistry registry, GraphContext graphContext, GenerationMode generationMode)
        {
			var arguments = new List<string>();
            
			for(int i = 0; i < inputSlots.Count; i++)
			{
                MaterialSlot slot = FindInputSlot<MaterialSlot>(inputSlots[i].id);
				arguments.Add(string.Format("{0} {1}", slot.concreteValueType.ToString(precision), slot.shaderOutputName));
			}

			for(int i = 0; i < outputSlots.Count; i++)
			{
                MaterialSlot slot = FindOutputSlot<MaterialSlot>(outputSlots[i].id);
				arguments.Add(string.Format("out {0} {1}", slot.concreteValueType.ToString(precision), slot.shaderOutputName));
			}

            if(arguments.Count == 0)
                return;

			var argumentString = string.Format("{0}({1})"
                    , GetFunctionName()
                    , arguments.Aggregate((current, next) => string.Format("{0}, {1}", current, next)));

            registry.ProvideFunction(GetFunctionName(), s =>
                {
                    s.AppendLine("void {0}", argumentString);
                    using (s.BlockScope())
                    {
                        s.AppendLine(code);
                    }
                });
        }

        public NeededCoordinateSpace RequiresNormal(ShaderStageCapability stageCapability)
        {
            var binding = NeededCoordinateSpace.None;
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
                binding |= slot.RequiresNormal();
            return binding;
        }

        public NeededCoordinateSpace RequiresViewDirection(ShaderStageCapability stageCapability)
        {
            var binding = NeededCoordinateSpace.None;
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
                binding |= slot.RequiresViewDirection();
            return binding;
        }

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            var binding = NeededCoordinateSpace.None;
            foreach (var slot in s_TempSlots)
                binding |= slot.RequiresPosition();
            return binding;
        }

        public NeededCoordinateSpace RequiresTangent(ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            var binding = NeededCoordinateSpace.None;
            foreach (var slot in s_TempSlots)
                binding |= slot.RequiresTangent();
            return binding;
        }

        public NeededCoordinateSpace RequiresBitangent(ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            var binding = NeededCoordinateSpace.None;
            foreach (var slot in s_TempSlots)
                binding |= slot.RequiresBitangent();
            return binding;
        }

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
            {
                if (slot.RequiresMeshUV(channel))
                    return true;
            }
            return false;
        }

        public bool RequiresScreenPosition(ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
            {
                if (slot.RequiresScreenPosition())
                    return true;
            }
            return false;
        }

        public bool RequiresVertexColor(ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
            {
                if (slot.RequiresVertexColor())
                    return true;
            }
            return false;
        }

        public VisualElement CreateSettingsElement()
        {
            PropertySheet ps = new PropertySheet();
            ps.style.width = 500;
            ps.Add(new DynamicSlotListView(this, inputSlots, SlotType.Input));
            ps.Add(new DynamicSlotListView(this, outputSlots, SlotType.Output));
            ps.Add(new PropertyRow(new Label("Enable Preview")), (row) =>
                {
                    row.Add(new Toggle(), (toggle) =>
                    {
                        toggle.value = m_DisplayPreview;
                        toggle.OnToggleChanged(ChangePreview);
                    });
                });
            return ps;
        }

        void ChangePreview(ChangeEvent<bool> evt)
        {
            owner.owner.RegisterCompleteObjectUndo("Change Preview state");
            m_DisplayPreview = evt.newValue;
            Dirty(ModificationScope.Graph);
        }
    }
}

